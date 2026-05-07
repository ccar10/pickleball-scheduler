# Court adjacency penalty for round-to-round mixing

## Problem

In a 16-player Whist round-robin, players reported that the same trio of 3 people landed on the same physical court in two consecutive rounds (observed multiple times during a Tuesday session). This is a court-labelling artifact — partner-once and opponent-twice math is preserved — but it makes the schedule feel poorly mixed.

## Mechanism

Two factors compound:

1. **Whist base round[0] = `(inf, 0, 1, 2)`.** The "infinity" player (always P0) plus three consecutive role-IDs. As rounds advance and roles rotate, adjacent rounds' match[0] share 3 players (P0 + the two middle roles carry over). No other base match has this — match[1..n] are built from non-consecutive roles and overlap by 0–1 players between adjacent rounds.

2. **`AssignCourtsToRound` is greedy per-round.** It minimizes per-player court-count spread and uses cyclic-shift only as a tiebreak. When P0's court counts dip below balanced, the min-spread permutation will put match[0] back on the same court it just played, dragging the trio with it. The cyclic-shift tiebreak never gets a vote because the spread term isn't tied.

This affects all canonical Whist sizes (8, 12, 16, 20, 24) since they all use `(inf, 0, 1, 2)` for base match[0]. It is most visible at 16 because that is the size in active use.

## Solution

Add an adjacency-overlap penalty to `ScorePermutation` so that the court permutation chosen each round avoids placing matches on a court where 3+ of their players appeared one round earlier.

### Score formula

Extending the existing scorer:

```
score = threeOverlapCount * 1_000_000_000
      + maxSpread        * 1_000_000
      + sumSpread        * 1_000
      + twoOverlapCount  * 100
      + shiftDistance
```

Where, for the candidate permutation in round r > 0:

- `threeOverlapCount` = number of courts c where the 4 players of (this round's match assigned to c) share 3 or 4 players with (last round's match assigned to c).
- `twoOverlapCount` = number of courts c where the same overlap is exactly 2.

Round 0 has no previous round, so both terms are 0 and behavior is unchanged.

### Priority ordering

| Term | Priority | Rationale |
|---|---|---|
| 3+ overlap (trio repeat) | Above max-spread | The visible user-pain. Must be avoided whenever any permutation allows it. |
| Max-spread | Existing | Per-player court balance — keep as the dominant fairness constraint when no trio-repeat exists. |
| Sum-spread | Existing | Existing tiebreak. |
| 2-overlap (pair repeat) | Below max-spread | Common in Whist; reduce when free, do not sacrifice court balance for it. |
| Shift distance | Existing | Existing rotational tiebreak for symmetric matchups. |

A 3-overlap is hard-blocked at the cost of any spread regression that opens up. For Whist 16 (4 courts), there are always at least 3 valid court labels for match[0] that yield zero trio overlap, so the spread cost is small in practice. We will measure the actual regression in tests rather than asserting an upper bound up front.

### Mechanics

- `AssignCourtsToRound` gains an optional parameter carrying the previous round's match-by-court mapping (e.g. `IReadOnlyList<Match>? previousRoundMatches` indexed by `CourtNumber - 1`, or a small `IReadOnlyDictionary<int, (int, int, int, int)>`).
- Both `ScheduleGenerator.Generate` and `GreedyScheduler.Generate` thread the prior round's matches into the call. Round 0 passes null.
- The cyclic-shift tiebreak is unchanged.
- No change to Whist matchup generation.

### Tests

1. **No trio repeat (n=16, 4 courts, 15 rounds).** Assert: for every court c and every adjacent round pair (r, r+1), the player overlap on court c is ≤ 2.
2. **Pair-repeat reduction.** Capture current count of 2-overlaps on same physical court as baseline; assert new count is ≤ baseline. (Establish baseline value during implementation.)
3. **Pairwise invariants preserved.** Every pair partners exactly once and opposes exactly twice across the 15 Whist rounds for n ∈ {8, 12, 16, 20, 24}.
4. **Court-spread bound documented.** Max per-player court-count spread across all canonical sizes stays at most one larger than the current bound. Measure during implementation; if regression is larger than expected, revisit weights before merging.
5. **Round 0 unchanged.** With null previous round, scoring matches existing behavior exactly.

## Out of scope

- Changing Whist base rounds to remove the `(inf, 0, 1, 2)` consecutive-role structure (alternative B). Larger redesign; revisit if the penalty approach proves insufficient.
- Round-order permutation as a post-pass (alternative C). Doesn't address the same-court mechanism.
- Bye-distribution improvements for odd-count round robins. Tracked separately.

## Risk

Low. The change is additive and gated by the new parameter; existing direct callers without the parameter retain current behavior. Math invariants of the Whist matchup generator are untouched.
