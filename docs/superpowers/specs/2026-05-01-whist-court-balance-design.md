# Court-Balanced Whist Cyclic Schedule

**Date:** 2026-05-01
**Status:** Draft (awaiting review)

## Background

The current Whist Cyclic generator produces matchups with the partner-once / opponent-twice invariants. It hands those matchups to the existing `AssignCourts` helper for court-number assignment. `AssignCourts` minimizes the sum of player-court-visit counts and breaks ties lex-first.

For symmetric configurations like 8 players × 2 courts × 7 rounds, every match-to-court permutation produces the same total imbalance. The lex-first tie-break consistently picks the same orientation, which leaves the fixed Whist player (`∞`, always in `baseRound[0]`) on a single court for the entire 7-round schedule — a `(7,0)` court split. End users have flagged this as a real fairness problem.

Earlier attempts to fix it inside `AssignCourts` (a per-player spread tie-breaker, and rotating the matches list before assignment) only shifted *which* player got stuck — they did not eliminate the structural imbalance.

The proper fix is to include explicit per-round, per-match court labels as part of the Whist data, so courts are no longer derived by a generic balancer that can't see the multi-round structure.

## Goals

1. For each Whist size in `{12, 16, 20, 24}`, every player's court-visit spread is `≤ 1` across the `n − 1`-round Whist cycle.
2. The fix is data-driven: court labels are hardcoded alongside base rounds, transcribed from a one-shot brute-force search living in the test project (same pattern as the existing base-round discovery).
3. A new unit test verifies the balance invariant for all four covered sizes; transcription bugs are caught at build time.
4. Schedules generated under non-Whist conditions are unaffected — the existing joint-search and `AssignCourts` paths handle them as today.

## Status: Closed Without Production Change

Implementation work proved that **cyclic Whist + per-player court spread ≤ 1 is structurally incompatible** at the smallest two sizes, and intractable to evaluate at the larger three within a reasonable compute budget. No production code changed; the search infrastructure was committed behind `[Theory(Skip="…")]` for future use. Findings:

### Wh(8) — Provably Impossible

In Wh(8), match 0's role-set is forced (by the partner-once / oppose-twice invariants) to be a (7, 3, 1) difference set in Z/7, i.e., a line of the Fano plane. Each non-`∞` player is in match 0 for a fixed 3-round subset (the orbit of their role under cyclic rotation). For per-player spread ≤ 1 the set `S` of "rounds match 0 plays court 0" would need `|R_j ∩ S| = 1` for every line `R_j`, but the Fano-plane incidence structure makes this impossible: with `|S| = 3`, exactly one Fano line is disjoint from `S` (a player at that role goes 1-6); with `|S| = 4`, `S` either contains a line entirely (a player goes 6-1) or is the complement of one (a player goes 0-7). Symmetric cases for other `|S|` likewise fail. The best achievable cyclic Wh(8) labeling gives spread 5 — a marginal improvement over today's (7, 0) that still leaves a stuck player. Not worth hardcoding.

### Wh(12) — Exhaustively Confirmed Impossible

A joint search enumerated all 220 distinct valid Wh(12) base rounds and ran an exhaustive court-label search against each. None admit a labeling with per-player spread ≤ 1. Cyclic Wh(12) is empirically as constrained as Wh(8), even after exhausting the alternative-base-round space.

### Wh(16, 20, 24) — Status Indeterminate

Inner court-label search is intractable per base round within a 30 minute wall-clock budget (`24¹⁵ / 120¹⁹ / 720²³` raw permutation trees with insufficient pruning). Could not determine feasibility within budget. A future attempt with sharper look-ahead pruning, or hours-to-days of compute per size, may resolve these.

### Outcome

All sizes continue to use the existing `AssignCourts` path; today's behavior is unchanged. The `WhistCyclicScheduleTests.GenerateBalancedSchedule` skipped Theory captures the full search infrastructure (base-round enumerator, court-label search with corrected `applied`-list undo, transcript helper) — a future contributor working on this can pick up directly without re-discovering the impossibility results above.

A future re-architecture using non-cyclic Whist or hand-tuned schedule families could fix the court-balance issue without violating the partner-once / oppose-twice invariants, but is out of scope for this spec.

## Non-Goals

- Best effort with verification: if a particular size in `{12, 16, 20, 24}` genuinely cannot achieve `spread ≤ 1` even after a joint search for `(base round, court labels)` (Q1 answer was option B), we accept `spread ≤ 2` for that size with a documented exception in the test.
- No labels for Wh(8) (see "Wh(8) Excluded" above).
- No randomness / variation per event in court labels — labels are deterministic per size, like the base rounds. The per-event input shuffle continues to randomize which player ends up at `∞` and other roles.
- No changes to the joint-search algorithm, the distribution test, the stats page, or the persistence model.

## Architecture

A new private record inside `WhistCyclicSchedule` carries the court labeling per round:

```csharp
private record RoundCourts(int[] Courts);  // Courts[i] = 0-based court index for matches[i] in this round
```

A new dictionary parallel to `BaseRounds` holds, for each supported size, an array of `n − 1` `RoundCourts` entries:

```csharp
private static readonly IReadOnlyDictionary<int, int[][]> CourtLabels =
    new Dictionary<int, int[][]>
    {
        [8]  = new int[][] { ... 7 rounds, each with 2 ints ... },
        [12] = new int[][] { ... 11 rounds, each with 3 ints ... },
        [16] = new int[][] { ... 15 rounds, each with 4 ints ... },
        [20] = new int[][] { ... 19 rounds, each with 5 ints ... },
        [24] = new int[][] { ... 23 rounds, each with 6 ints ... },
    };
```

Total table size: 7×2 + 11×3 + 15×4 + 19×5 + 23×6 = 14+33+60+95+138 = 340 ints. Trivial.

The data is transcribed from the brute-force search output (a `[Theory(Skip="...")]` test in `WhistCyclicScheduleTests.cs`).

## `GetRoundMatchups` changes

Currently:

```csharp
public static List<Match> GetRoundMatchups(List<Player> players, int roundIndex)
{
    // ... validate, resolve roles, return matches with no CourtNumber set ...
}
```

After this change:

```csharp
public static List<Match> GetRoundMatchups(List<Player> players, int roundIndex)
{
    // ... validate, resolve roles ...
    var labels = CourtLabels[players.Count][roundIndex];
    for (int i = 0; i < matches.Count; i++)
        matches[i].CourtNumber = labels[i] + 1;
    return matches;
}
```

`ScheduleGenerator.Generate` no longer calls `AssignCourts` when the Whist branch fires:

```csharp
if (useWhist)
{
    matches = WhistCyclicSchedule.GetRoundMatchups(players, r);
    // CourtNumber already set; AssignCourts not called.
}
```

`AssignCourts` itself is untouched — it still runs for all non-Whist round generation.

## Search algorithm (test project)

A new `[Theory(Skip = "one-shot generator")]` method `GenerateCourtLabels` in `WhistCyclicScheduleTests.cs`. Like the existing `GenerateBaseRound` helper:

For each size:

1. Build the full `n − 1`-round schedule using the existing `BaseRounds` data.
2. Recursively label rounds 0 through `n − 2`. For each round, try the `(n/4)!` permutations of court indices.
3. Track per-player per-court visit counts; prune any branch where some player's count for any court would exceed `ceil((n − 1) / (n/4))`.
4. Heuristic: try permutations in order that gives the most-disadvantaged player their least-played court first.
5. On success (every player's spread ≤ 1), emit a paste-ready C# transcript via `Assert.Fail` listing the labelings round-by-round.

If no court labeling exists for the current base round (genuinely possible for some Whist constructions):

- Outer loop regenerates the base round via the existing brute-force base-round helper, then re-runs the court search.
- Continue until a `(base round, court labels)` pair satisfies both Whist invariants AND court-balance.

If no joint pair is found within a reasonable budget (say, 1 hour wall-clock per size), accept `spread ≤ 2` for that size and note it in the spec.

Search space:

| Size | Per-round permutations | Total raw | Notes |
|------|------------------------|-----------|-------|
| 8 | 2 | 128 | Trivial |
| 12 | 6 | 6¹¹ ≈ 360M | Pruning makes this seconds |
| 16 | 24 | 24¹⁵ ≈ 2.8e20 | Pruning critical; minutes expected |
| 20 | 120 | 120¹⁹ | Hours |
| 24 | 720 | 720²³ | Hours-to-days; budget cap applies |

Aggressive per-round pruning (rejecting any partial that exceeds the ceiling for any player) collapses the practical search dramatically below the raw numbers.

## Tests

### Existing test — unchanged behavior

`AllPairsPartnerOnceAndOpposeTwice` still passes for all 5 sizes. Adding court labels does not change which players are in which matches, only which court each match plays on.

### New test — court balance

```csharp
[Theory]
[InlineData(8)]
[InlineData(12)]
[InlineData(16)]
[InlineData(20)]
[InlineData(24)]
public void EachPlayerCourtVisitsAreBalanced(int playerCount)
{
    var players = Enumerable.Range(1, playerCount)
        .Select(id => new Player { Id = id, Name = $"P{id}" })
        .ToList();
    var courts = playerCount / 4;

    var visits = players.ToDictionary(p => p.Id, _ => new int[courts]);

    for (int r = 0; r < playerCount - 1; r++)
    {
        var matches = WhistCyclicSchedule.GetRoundMatchups(players, r);
        foreach (var m in matches)
        {
            var courtIdx = m.CourtNumber - 1;
            foreach (var pid in new[] { m.Team1Player1Id, m.Team1Player2Id, m.Team2Player1Id, m.Team2Player2Id })
                visits[pid][courtIdx]++;
        }
    }

    foreach (var (pid, counts) in visits)
    {
        var max = counts.Max();
        var min = counts.Min();
        Assert.True(max - min <= 1,
            $"Player {pid} court visits {string.Join(",", counts)} — spread {max - min} > 1");
    }
}
```

If a size was accepted with `spread ≤ 2` exception, that case asserts `≤ 2` instead and the test name or comment notes it.

### Stats-page side effect

`EventStats.razor`'s Court Visits table will read the new balanced layouts from persisted matches without any code changes — every player at `(4, 3)` or `(3, 4)` for 8p/2c, etc.

## Files Changing

- Modify: `PickleballScheduler/Services/WhistCyclicSchedule.cs` — add `CourtLabels` dictionary, set `CourtNumber` in `GetRoundMatchups`.
- Modify: `PickleballScheduler/Services/ScheduleGenerator.cs` — remove the `AssignCourts` call from the Whist branch.
- Modify: `PickleballScheduler.Tests/Services/WhistCyclicScheduleTests.cs` — add the court-balance invariant test, add the skipped one-shot court-label generator.

## Open Questions Resolved During Brainstorm

- **Goal threshold:** strict `≤ 1` ideally, with `≤ 2` accepted per size if joint search proves it impossible (option B).
- **Implementation approach:** one-shot brute-force search transcribed into source as hardcoded data (option A).
- **Court labeling shape:** one full per-round permutation for each round (no rotation rule) — most flexible, table size is fine.
- **Joint vs sequential search:** start with the existing base round; only regenerate the base round if no court labeling works for it.
- **Per-event variability:** none for court labels; the existing input-shuffle at `EventSetup` keeps player roles randomized per event.
