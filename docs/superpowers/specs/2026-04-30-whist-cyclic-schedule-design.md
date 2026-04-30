# Whist Cyclic Schedule Integration

**Date:** 2026-04-30
**Status:** Draft (awaiting review)

## Background

The current generator (joint round search with beam-width-1 fallback at active > 9) produces schedules with HR1=0 and HR2=0 for Paul's 8p/2c/10r case, but it does not guarantee equal opponent distribution across the first `n-1` rounds. Specifically, for the canonical sizes where a perfect Whist tournament exists, our schedules have measurable opponent imbalance that the user noticed in real play.

A **Whist tournament** is a round-robin design where, for `n = 4k` players over `n-1` rounds:
- Every pair of players partners exactly once.
- Every pair of players opposes exactly twice.
- Each player plays in every round (no byes).

Whist tournaments exist for all `n = 4k`. The **Whist Cyclic** construction generates a Whist tournament from a single carefully-chosen base round by holding one player fixed (`∞`) and rotating the other `n-1` players cyclically. Anderson (1973, 1997) and Moore (1896) catalog base rounds for the standard sizes.

## Goals

1. Use a perfect Whist Cyclic schedule for the first `n-1` rounds whenever the configuration allows: `playerCount ∈ {8, 12, 16, 20, 24}`, `courts ≥ playerCount/4`, `rounds ≥ playerCount-1`.
2. Fall back to the existing joint search for any rounds beyond `n-1`, and for any configuration that doesn't qualify.
3. Verify the published base rounds against the Whist invariants in a unit test, so transcription errors are caught at build time.

## Non-Goals

- Whist sizes beyond 24 (YAGNI for pickleball events).
- Whist for partial round counts (`rounds < n-1`) — fall back entirely to joint search; partial Whist doesn't deliver the partner-once / opponent-twice guarantee anyway.
- Optimizing post-Whist rounds. Joint search's beam-width-1 behavior is acceptable when seeded with the perfect coverage from the Whist cycle.
- Mixed Doubles / Fixed Partners format integration — their own brainstorm covers that.
- Visualizing court rotation fairness — `AssignCourts` continues to handle it.

## Architecture

A new internal static helper `WhistCyclicSchedule` exposes two public methods:

```csharp
internal static class WhistCyclicSchedule
{
    public static bool IsSupportedSize(int playerCount);
    public static List<Match> GetRoundMatchups(List<Player> players, int roundIndex);
}
```

`IsSupportedSize` returns `true` for `playerCount ∈ {8, 12, 16, 20, 24}`.

`GetRoundMatchups` returns a list of `Match` objects (with team player IDs set, court number unset) for round `roundIndex` (0-based, max `playerCount - 2`). Court numbers are assigned downstream by the existing `AssignCourts` method.

`ScheduleGenerator.Generate` checks at the top of its per-round loop: if Whist applies for this round, use `GetRoundMatchups`; otherwise use the existing `BuildRound` joint search. The HR1/HR2 counters and tracking-update block downstream are unchanged — they observe Whist matchups the same way they observe joint-search matchups.

## Player Labeling and Rotation

For `n = 4k` players, label them as one **fixed** player (`∞`) plus `n-1` rotating players indexed `0..n-2`. The first player in the input list is `∞`; the rest are `0..n-2` in input order.

A `BaseMatch` literal in the source data declares the partner pairs in each match of the base round, using role labels — either `"inf"` or a numeric index.

For round `r` (0-indexed), each rotating role `i` resolves to `players[1 + ((i + r) mod (n-1))]`, and `"inf"` resolves to `players[0]`.

Round 0 is the base round (no rotation).

## Base Round Data

Five base rounds are hardcoded as static data, transcribed from Anderson, *Combinatorial Designs and Tournaments* (1997) or equivalent published Whist tournament catalogs. Each base round defines `n/4` matches.

The exact role assignments are filled in during implementation. The unit test below verifies them.

| Player count | Matches | Source |
|---|---|---|
| 8 | 2 | Anderson 1997, Wh(8) |
| 12 | 3 | Anderson 1997, Wh(12) |
| 16 | 4 | Anderson 1997, Wh(16) |
| 20 | 5 | Anderson 1997, Wh(20) |
| 24 | 6 | Anderson 1997, Wh(24) |

## Integration with `Generate`

In `ScheduleGenerator.Generate`, at the top of the round loop, replace the unconditional `BuildRound` call with a Whist-vs-joint-search branch:

```csharp
List<Match> matches;
bool useWhist =
    WhistCyclicSchedule.IsSupportedSize(players.Count) &&
    numberOfCourts >= players.Count / 4 &&
    numberOfRounds >= players.Count - 1 &&
    r < players.Count - 1;

if (useWhist)
{
    matches = WhistCyclicSchedule.GetRoundMatchups(players, r);
    AssignCourts(matches, courtCounts, matchesPerRound);
}
else
{
    var roundResult = BuildRound(activePlayers, players, partnerCounts, opponentCounts,
        lastOpponentRound, courtCounts, r, matchesPerRound);
    matches = roundResult.Matches;
}
```

The HR1/HR2 counting blocks, tracking-update block, and bye/round-add code are unchanged.

For `r >= playerCount - 1` (long-format events), joint search resumes seeded with the post-Whist partner and opponent dictionaries (every pair at count 1 partner / 2 opponent). The existing lex-cost tuple drives sensible later rounds from there.

When Whist applies, every player plays every round, so `SelectActivePlayers` returns all players (the existing `playersPerRound = matchesPerRound * 4` calculation already covers this for `courts ≥ playerCount/4`).

## Court Assignment

Whist provides matchups only — no court labels. The existing `AssignCourts` permutes court numbers within each round to balance per-player court-visit counts across the schedule. For Whist rounds this still gives each player roughly equal time on each court (each player plays `playerCount-1` rounds, distributed across `playerCount/4` courts).

## Counters and Banner

By construction, Whist rounds contribute zero to `Hr1Violations` and zero to `Hr2Violations`. For configurations where Whist runs the entire schedule (`rounds == playerCount-1`), the banner stays hidden. For long-format events with rounds beyond Whist, joint search may add small contributions; the banner shows if and only if the totals exceed zero — same behavior as today.

The repeat-suggestion calculator (`TrySuggestZeroViolationConfig`) already calls `Generate` for near-neighbor configurations; no change needed. Whist will simply be tried automatically for any neighbor that qualifies.

## Files Changing

- New: `PickleballScheduler/Services/WhistCyclicSchedule.cs` — `IsSupportedSize`, `GetRoundMatchups`, the 5 base-round tables, the rotation helper.
- Modify: `PickleballScheduler/Services/ScheduleGenerator.cs` — the per-round branch from above.
- New: `PickleballScheduler.Tests/Services/WhistCyclicScheduleTests.cs` — the invariant test.
- Modify: `PickleballScheduler.Tests/Services/ScheduleGeneratorTests.cs` — integration tests for Paul's case + fallback test for an unsupported config.

## Testing Strategy

### Whist invariant test (the safety net)

`WhistCyclicScheduleTests` runs `[Theory]` over `{8, 12, 16, 20, 24}`. For each size:

1. Build the full `n-1`-round schedule by calling `GetRoundMatchups(players, r)` for each `r`.
2. Aggregate partner counts: assert every pair has count exactly 1.
3. Aggregate opponent counts: assert every pair has count exactly 2.
4. For each round: assert exactly `n/4` matches, no player double-booked, all `n` players represented.

If a base-round transcription is wrong, this test fails with a specific message (e.g., "pair (3, 5) partners 2 times" or "round 4 missing player 7").

### Integration test for Paul's case

Extend `Generate_8Players_2Courts_10Rounds_NoConsecutiveOpponents` (or add a sibling test) to assert:

1. Across rounds 1–7, every pair partners exactly once.
2. Across rounds 1–7, every pair opposes exactly twice.
3. Across all 10 rounds, `Hr1Violations == 0 && Hr2Violations == 0`.

### Fallback test

Add a test for `10 players, 2 courts, 9 rounds` (unsupported size). Assert the schedule still generates without exceptions and the joint search runs (verified indirectly by checking schedule structural validity).

The existing distribution theory test continues to exercise non-Whist configs as today; no changes required there.

## Open Questions Resolved During Brainstorm

- **Player count scope:** 8, 12, 16, 20, 24.
- **Round count constraint:** Whist applies only when `rounds ≥ playerCount-1`. Shorter round counts use joint search.
- **Court count constraint:** Whist applies when `courts ≥ playerCount/4`. Surplus courts go unused.
- **Court rotation handling:** Whist provides matchups only; existing `AssignCourts` handles court numbers.
- **Approach chosen:** hardcoded base rounds + runtime cyclic rotation (Approach 1). Not the cyclic constructor (Approach 2 — overkill) and not pre-baked complete schedules (Approach 3 — bigger tables, no benefit).
