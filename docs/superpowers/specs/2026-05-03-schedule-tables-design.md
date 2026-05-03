# Table-Driven Schedule Generator

**Date:** 2026-05-03
**Status:** Draft (awaiting review)

## Background

The current scheduler has accumulated significant complexity:

- A `WhistCyclicSchedule` path for sizes `{8, 12, 16, 20, 24}` with sufficient courts and rounds (cyclic rotation of a hardcoded base round).
- A general-purpose recursive backtracking path (`ScheduleGenerator.SearchMatches`) ranked by a 5-level lex `CostTuple(Hr1, Hr2, partnerSq, opponentSq, courtImbalance)` with beam pruning at active size > 9.
- A separate `AssignCourts` pass that enumerates `(n/4)!` court permutations per round, optimizing total imbalance with a per-player spread tie-break.
- HR1 (forced partner repeat) and HR2 (consecutive opponent repeat) violation tracking, persisted on `Event`, surfaced as a banner on the stats page.
- A `TrySuggestZeroViolationConfig` near-neighbor search that re-runs the generator across 49 player/round combinations whenever violations exist.
- A recently-completed investigation (`2026-05-01-whist-court-balance-design.md`) that proved the cyclic Whist construction is structurally incompatible with per-player court spread ≤ 1 at sizes 8 and 12, and shipped no production change.

The friend who uses this app explicitly noted that Whist was just a suggestion to help, not a requirement. Real events are a fair distribution over R rounds at any size — R can comfortably exceed `n − 1` (Paul Verrier's documented use is 8 players × 2 courts × 10 rounds).

The runtime search is doing work that can be done once, offline. The HR1/HR2 framing dressed up soft preferences as hard rules. The court-balance investigation showed the runtime court permuter cannot fix structural multi-round imbalances. All three observations point to the same simplification.

## Goals

1. Replace the runtime search and Whist cyclic path with five hand-tuned static schedule tables, one per canonical `(playerCount, courtCount)` pair: `(8,2), (12,3), (16,4), (20,5), (24,6)`.
2. Each table is 30 rounds long. Tables satisfy:
   - Zero consecutive partner repeats (no pair partners in both round R and round R+1).
   - Partner-count spread ≤ 1 across all 30 rounds.
   - Per-player court-visit spread ≤ 1 across all 30 rounds.
   - Opponent-encounter spread minimized (best effort, not a strict bound).
3. Off-canonical configurations (any `(playerCount, courtCount)` not in the canonical set) fall through to a small deterministic runtime greedy.
4. `Hr1Violations`, `Hr2Violations`, `RepeatSuggestion`, the EventStats violation banner, and the `TrySuggestZeroViolationConfig` neighbor scan are removed entirely.
5. The stats page partner/opponent/court matrices continue to work unchanged — they read from persisted matches.

## Non-Goals

- Tables for off-canonical sizes (7, 9, 10, 11, 13, 14, 15, 17, 18, 19, 21, 22, 23). These use the greedy fallback. Quality at these sizes is "good enough," not table-grade.
- Tables for canonical player counts on non-natural court counts (e.g., 16 players on 3 courts). These also use the greedy fallback.
- Preserving historical `Hr1Violations` / `Hr2Violations` data. The columns are dropped; existing values are wiped.
- Schedule quality summary on the stats page. The banner is removed entirely; matrices remain.
- Support for R > 30. Hosts requesting more than 30 rounds is out of scope; the UI clamps at 30 (current max is already constrained).
- Per-event variability of the canonical table itself. The per-event input shuffle randomizes which physical player ends up at each role index — that is the only randomization.

## Architecture

### Runtime flow

```
ScheduleGenerator.Generate(players, courts, rounds):
    if (players.Count, courts) is canonical:
        for r in 0 .. rounds-1:
            matches = CanonicalSchedules.GetRound(players.Count, r, players)
            append round r+1 with these matches and zero byes
    else:
        run GreedyScheduler over players for `rounds` rounds
    return ScheduleResult(rounds-list)
```

The input `players` list ordering is the role assignment for the canonical path: `players[0]` plays role 0 in the table, `players[1]` plays role 1, etc. Per-event randomization is performed upstream in `EventSetup.razor` before `Generate` is called (same pattern as today). `ScheduleGenerator` does not shuffle internally.

No backtracking, no cost tuple, no court permutation search, no violation counting, no neighbor scan.

### Components

**`Services/CanonicalSchedules.cs`** (new). Holds the five static tables. Public surface:

```csharp
public static class CanonicalSchedules
{
    public static bool IsCanonical(int playerCount, int courtCount);
    public static List<Match> GetRound(int playerCount, int roundIndex, List<Player> shuffledPlayers);
}
```

Internal data:

```csharp
private record BakedMatch(int RoleA, int RoleB, int RoleC, int RoleD, int Court);

private static readonly IReadOnlyDictionary<int, BakedMatch[][]> Schedules =
    new Dictionary<int, BakedMatch[][]>
    {
        [8]  = /* 30 rounds × 2 matches */ ,
        [12] = /* 30 rounds × 3 matches */ ,
        [16] = /* 30 rounds × 4 matches */ ,
        [20] = /* 30 rounds × 5 matches */ ,
        [24] = /* 30 rounds × 6 matches */ ,
    };

private static readonly HashSet<(int players, int courts)> CanonicalPairs =
    new() { (8, 2), (12, 3), (16, 4), (20, 5), (24, 6) };
```

`GetRound` resolves role indices against `shuffledPlayers` and returns `Match` objects with `Team1Player1Id`, `Team1Player2Id`, `Team2Player1Id`, `Team2Player2Id`, `CourtNumber = Court + 1`.

**`Services/GreedyScheduler.cs`** (new internal). Algorithm per round:

1. Select active players: prefer those with the highest accumulated bye count; tie-break by player ID. This is the same logic as today's `SelectActivePlayers`.
2. While unmatched active players remain:
   - Pick player A with the lowest unused player ID.
   - Pick partner B from remaining unused players, minimizing `priorPartnerCount[A,B]`. Tie-break: minimize `priorOpponentCount[A,B]`. Tie-break: lowest ID.
   - Pick opponent pair `(C,D)` from remaining unused, minimizing `sum_{x∈{A,B}, y∈{C,D}} priorOpponentCount[x,y]`. Tie-break: minimize `priorPartnerCount[C,D]`. Tie-break: lowest IDs lex.
   - Pick court: from courts not yet used this round, pick one minimizing `sum_{p∈{A,B,C,D}} priorCourtCount[p, court]`. Tie-break: lowest court index.
   - Mark `{A,B,C,D}` used; record the match.
3. Update `priorPartnerCount`, `priorOpponentCount`, `priorCourtCount` after the round is fully built.

The greedy is deterministic: same input produces the same output. No randomization needed (input shuffle already happens upstream in `Generate`).

**`Services/ScheduleGenerator.cs`** (rewritten). Reduces to:

```csharp
public ScheduleResult Generate(List<Player> players, int courts, int rounds)
{
    if (CanonicalSchedules.IsCanonical(players.Count, courts))
        return GenerateFromTable(players, courts, rounds);
    else
        return GreedyScheduler.Generate(players, courts, rounds);
}
```

Plus the two helpers. ~80 lines total. `SearchMatches`, `BuildRound`, `RoundCandidate`, `IsHr1Violation`, `IsHr2Violation`, `AssignCourts`, `PermuteAndScore`, `CalculateImbalance`, `TrySuggestZeroViolationConfig`, `BeamWidthLargeActive`, `BeamThreshold` all deleted.

### Data flow change

Before: `Generate` produced a `ScheduleResult` containing rounds + violation counts + suggestion. The result was persisted on `Event` as columns alongside `Rounds`.

After: `Generate` produces a `ScheduleResult` containing only rounds. The `Event` model loses two columns.

## Schedule Result and Event Model Changes

### `Services/ScheduleResult.cs`

Before:
```csharp
public record ScheduleResult(
    List<Round> Rounds,
    int Hr1Violations,
    int Hr2Violations,
    string? RepeatSuggestion);
```

After:
```csharp
public record ScheduleResult(List<Round> Rounds);
```

### `Models/Event.cs`

Remove the two columns:
```csharp
public int Hr1Violations { get; set; } = 0;   // remove
public int Hr2Violations { get; set; } = 0;   // remove
```

### EF migration

Add a new migration `RemoveScheduleViolations` that:
- Drops `Hr1Violations` and `Hr2Violations` columns from `Events`.
- Has the standard `Down` that re-adds them with default 0 (so the migration is reversible).

Existing rows lose their violation counts. This is intentional and accepted.

### `EventService.cs`

Any code that copies `result.Hr1Violations` / `result.Hr2Violations` onto the `Event` entity is removed. Any code that reads them for display is removed.

## UI Changes

### `Components/Pages/EventSetup.razor`

The post-generate block (line ~339) currently runs:
```csharp
if (result.Hr1Violations > 0 || result.Hr2Violations > 0)
{
    var suggestion = ScheduleGenerator.TrySuggestZeroViolationConfig(...);
    var resultWithSuggestion = result with { RepeatSuggestion = suggestion };
    ...
}
```

This block is removed entirely. The result of `Generate` is consumed as-is.

### `Components/Pages/EventStats.razor`

The violation banner (lines ~20–35):
```razor
@if (evt.Hr1Violations > 0 || evt.Hr2Violations > 0)
{
    ... <strong>@evt.Hr1Violations</strong> early partner repeat(s) ...
    ... <strong>@evt.Hr2Violations</strong> back-to-back opponent matchup(s) ...
    @if (!string.IsNullOrEmpty(evt.RepeatSuggestion)) { ... }
}
```

is removed. The page proceeds directly to the partner / opponent / court matrices, which already exist and read from persisted matches.

## Greedy Fallback Detail

Used when `(playerCount, courtCount)` is not in the canonical set. Examples:
- 7 players, 1 court (3 byes per round)
- 14 players, 3 courts (2 byes per round)
- 16 players, 2 courts (8 byes per round — covers a host running a small event with limited courts)
- 9 players, 2 courts (1 bye per round)

Algorithm operates round-by-round with three running counters keyed on player ID and pair key:
- `partnerCount[pairKey] : int`
- `opponentCount[pairKey] : int`
- `courtCount[playerId, courtIndex] : int`
- `byeCount[playerId] : int`

Per-round procedure as described in Architecture > Components above. No look-ahead, no backtracking. Deterministic given player order and counters.

Quality expectations:
- For `(playerCount mod 4 == 0)` cases on canonical court counts, falls under canonical path; does not invoke greedy.
- For odd / non-multiple-of-4 sizes, greedy provides "no consecutive partner repeats in most cases" and "balanced enough" partner/opponent/court distribution. No formal bound.
- If the friend reports quality issues at a specific off-canonical size, that size can be promoted to a hand-tuned table later. Tables and greedy live side-by-side; promotion is additive.

## Offline Table Generation

A `[Theory(Skip = "one-shot table generator")]` method in `PickleballScheduler.Tests/Services/CanonicalScheduleGenerator.cs`. For each canonical `(n, c)`:

1. **Initial schedule:** generate a random valid 30-round schedule. Each round is a perfect matching of `n` players into `c` matches of 4. Court labels assigned 0..c−1 to matches in order initially.
2. **Cost function:**
   ```
   cost = consecutiveRepeats * 1e6
        + partnerSpread^2 * 1000
        + courtSpread^2 * 1000
        + opponentCountVariance
   ```
   where `consecutiveRepeats` counts pairs that partner in adjacent rounds, `partnerSpread = max - min` of pair partner counts across the 30 rounds, `courtSpread = max over players of (max_court_visits - min_court_visits)`, and `opponentCountVariance` is the variance of opponent-encounter counts across all `C(n, 2)` pairs.
3. **Local moves:**
   - Swap two players within the same round (move them between matches or swap their roles within a match).
   - Swap court assignments between two matches in the same round.
   - Swap two whole rounds.
4. **Simulated annealing:** start temperature 100, geometric cooling 0.9999, target ~30 seconds compute per size. Accept moves with probability `min(1, exp(-Δcost / T))`.
5. **Termination:** stop when cost reaches 0 (all hard goals met) or after the time budget.
6. **Output:** emit a paste-ready C# initializer in the `Assert.Fail` message:
   ```
   [8] = new BakedMatch[][] {
       new[] { new BakedMatch(0, 1, 2, 3, 0), new BakedMatch(4, 5, 6, 7, 1) },
       ...
   },
   ```
7. **Verification:** developer pastes into `CanonicalSchedules.cs`. The build-time `CanonicalSchedulesTests` (see below) catch any transcription errors immediately.

If a size fails to reach `consecutiveRepeats == 0 && partnerSpread <= 1 && courtSpread <= 1` within budget, the spec is amended for that size with a documented relaxation (e.g., `courtSpread <= 2`) and a note in the source. This is the same escape valve the Whist court-balance spec used.

## Tests

### `CanonicalSchedulesTests` (new)

For each canonical size `n ∈ {8, 12, 16, 20, 24}`:

- **`TableHasCorrectShape`**: 30 rounds, each round has `n/4` matches, every role index in `[0, n)` appears exactly once per round, every court index in `[0, n/4)` appears exactly once per round.
- **`NoConsecutivePartnerRepeats`**: for r in 0..28, no pair partners in both round r and round r+1.
- **`PartnerSpreadAtMostOne`**: max - min of pair partner counts across all rounds is ≤ 1.
- **`CourtSpreadAtMostOne`**: for every player, max - min of per-court visit counts across all rounds is ≤ 1. (Or whatever relaxation was documented for that size.)

### `GreedySchedulerTests` (new)

- **`OffCanonicalSizesProduceValidSchedule`**: for `(7,1,8)`, `(9,2,10)`, `(14,3,8)`, `(10,2,10)` — verify every round has correct match count, no player plays twice in same round, no consecutive partner repeats (best effort: assert ≤ 1 repeat across the schedule).
- **`Deterministic`**: same input twice produces identical output.

### `ScheduleGeneratorTests` (existing, rewritten)

- Drop tests of `SearchMatches`, `BuildRound`, `IsHr1Violation`, `IsHr2Violation`, `AssignCourts`, `TrySuggestZeroViolationConfig`.
- Keep / rewrite tests for the public `Generate` contract: returns the right number of rounds, every match has 4 distinct players, byes accounted for.
- Add a regression test: for each canonical `(n, c)`, `Generate` returns the same matchups as direct `CanonicalSchedules.GetRound` (modulo the player shuffle being deterministic per seed).

### `ScheduleDistributionTests` (existing, adapted)

The current distribution test checks that across many random runs, certain statistical properties hold. Rewrite to check that:
- For canonical configs, all runs produce structurally equivalent schedules (just different player → role assignments).
- For off-canonical configs, the greedy produces consistent quality across random shuffles.

### `ScheduleFeasibilityTests` / `ScheduleFeasibility` / `HardRuleHelperTests` / `CostTupleTests` (existing)

These all relate to HR1/HR2 / cost tuple machinery. Delete entirely.

## Files Changing

| Action | Path |
|---|---|
| Delete | `PickleballScheduler/Services/WhistCyclicSchedule.cs` |
| Delete | `PickleballScheduler/Services/CostTuple.cs` |
| Add | `PickleballScheduler/Services/CanonicalSchedules.cs` |
| Add | `PickleballScheduler/Services/GreedyScheduler.cs` |
| Modify | `PickleballScheduler/Services/ScheduleGenerator.cs` (strip to dispatcher) |
| Modify | `PickleballScheduler/Services/ScheduleResult.cs` (drop violation fields) |
| Modify | `PickleballScheduler/Services/EventService.cs` (drop violation copy/read) |
| Modify | `PickleballScheduler/Models/Event.cs` (drop two columns) |
| Add | `PickleballScheduler/Migrations/<timestamp>_RemoveScheduleViolations.cs` |
| Modify | `PickleballScheduler/Components/Pages/EventSetup.razor` (remove suggest-config block) |
| Modify | `PickleballScheduler/Components/Pages/EventStats.razor` (remove violation banner) |
| Delete | `PickleballScheduler.Tests/Services/CostTupleTests.cs` |
| Delete | `PickleballScheduler.Tests/Services/HardRuleHelperTests.cs` |
| Delete | `PickleballScheduler.Tests/Services/ScheduleFeasibility.cs` |
| Delete | `PickleballScheduler.Tests/Services/ScheduleFeasibilityTests.cs` |
| Delete | `PickleballScheduler.Tests/Services/WhistCyclicScheduleTests.cs` |
| Modify | `PickleballScheduler.Tests/Services/ScheduleGeneratorTests.cs` (trim to dispatcher contract) |
| Modify | `PickleballScheduler.Tests/Services/ScheduleDistributionTests.cs` (adapt) |
| Add | `PickleballScheduler.Tests/Services/CanonicalSchedulesTests.cs` |
| Add | `PickleballScheduler.Tests/Services/GreedySchedulerTests.cs` |
| Add | `PickleballScheduler.Tests/Services/CanonicalScheduleGenerator.cs` (skipped one-shot generator) |

## Implementation Order

1. Add `CanonicalSchedules.cs` skeleton (empty tables) and `GreedyScheduler.cs`.
2. Rewrite `ScheduleGenerator.Generate` as the dispatcher.
3. Drop `ScheduleResult` violation fields, update `EventService` and the two Razor pages.
4. Add EF migration to drop the two `Event` columns.
5. Delete dead code and tests.
6. Add `GreedySchedulerTests` and `CanonicalSchedulesTests` (the latter will fail until tables are populated).
7. Add the offline `CanonicalScheduleGenerator` test, run it once per size, paste outputs into `CanonicalSchedules.cs`.
8. Verify all tests pass; smoke-test the running app at canonical and off-canonical sizes.

## Risks and Mitigations

- **SA generator fails to converge for some size within 30s.** Increase budget, or document a relaxed bound for that size and amend the test. Same fallback the Whist court-balance spec used.
- **Greedy quality is poor at some commonly-used off-canonical size.** Promote that size to a hand-tuned table. Architecture supports this without changing the dispatcher.
- **Existing event regeneration.** The change does not regenerate persisted schedules. Old events display from their stored matches as before. Only the violation banner disappears (because the columns are dropped).
- **Stats matrices verifying schedule quality.** Paul's quality bar is met by the canonical tables by construction. The stats page partner/opponent/court matrices give him the same view he had before, just without the violation banner on top.
