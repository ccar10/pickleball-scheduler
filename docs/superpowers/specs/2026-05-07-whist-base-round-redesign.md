# Redesign Whist base rounds for fast court coverage

## Problem

Players using a 16-person Whist round-robin reported the same trio of people landing on the same court multiple times in a row, and "seeing the same faces too often" overall. The deeper cause is structural, not a court-labelling artifact:

The current base round for every canonical size (8, 12, 16, 20, 24) places the "infinity" player (P0) in a match with three *consecutive* roles `(inf, 0, 1, 2)`. Because role IDs rotate by +1 each round, P0's three court-mates each round are `(P_{r+1}, P_{r+2}, P_{r+3})` — only one new player enters per round. P0 doesn't share a court with every other player until late in the schedule.

For n=16, P0 first shares a court with all 15 other players at round 13 of 15. The theoretical minimum is ⌈15 / 3⌉ = 5 rounds. The current design is ~2.6× the achievable minimum for the worst-case player.

## Goal

For each canonical size n ∈ {8, 12, 16, 20, 24}, replace the hardcoded base round in `WhistMatchups.cs` with one that **minimizes the maximum round at which any player has shared a court with all n-1 other players**, while preserving the existing Whist invariants (every pair partners exactly once, every pair opposes exactly twice across n-1 rounds).

### Optimization metric

For a base round, define `coverageRound(p)` = the smallest round R such that across rounds 0..R, player p has shared a court with every other player at least once. Define `maxCoverage` = max over all p. Minimize `maxCoverage`. Tiebreak by sum of `coverageRound(p)` across all players (lower is better).

### Lower bounds

The theoretical floor is `ceil((n-1) / 3)` rounds for any player (each round provides at most 3 new court-mates). Achievability depends on combinatorial design; we'll learn how close we can get during search:

| n | Floor (rounds) |
|---|---|
| 8 | 3 |
| 12 | 4 |
| 16 | 5 |
| 20 | 7 |
| 24 | 8 |

## Solution approach

A one-time offline search produces a concrete base round per size; results are hand-coded into `WhistMatchups.cs`. The search itself lives in the test project (or a small console tool) and is not run on every build.

### Search

For each size n:

1. Enumerate candidate base rounds with symmetry-breaking conventions to shrink the space (e.g. fix inf in match[0], partner inf with role 0, sort matches by smallest role, sort roles within teams).
2. For each candidate, verify Whist validity via difference-class analysis:
   - **Partner pairs (finite-only):** the n/2 - 1 finite partner pairs in the base round, taken as differences mod (n-1), must be a permutation of {1, 2, ..., (n-1)/2} where d and (n-1)-d are equivalent. (One difference class per finite partner pair, no repeats — equivalent to "every finite partner pair appears exactly once across all rotations.")
   - **Opponent pairs (finite-only):** the n - 2 finite opponent pairs, taken as differences mod (n-1), must hit each difference class exactly twice.
3. For each valid candidate, compute `coverageRound(p)` for all players and `maxCoverage`.
4. Keep the candidate with smallest `maxCoverage`, breaking ties by sum of coverage rounds, then lexicographically.

### Search feasibility

Search space sizes are tractable for the canonical sizes when symmetries are broken. n=16 has on the order of low millions of candidates after symmetry-breaking. The runtime budget is "minutes per size at most" — if the naive enumeration is too slow we can prune more aggressively (e.g. enforce difference-class constraints during construction, not after).

### Output

A small number of integer arrays — one base round per size — committed as a code change in `WhistMatchups.cs`. The search code lives separately; the production code only sees the result.

## What stays the same

- The rotational structure of `WhistMatchups.GetRoundMatchups`. Only the base round payloads change.
- `ScheduleGenerator` and `GreedyScheduler`. They consume the base rounds via the existing API.
- The court permutation logic in `AssignCourtsToRound`. Same-court-same-trio adjacency is no longer expected to be common (the structural cause is removed) — if it persists in measurement, we revisit, but it's out of scope here.
- All existing public APIs and call sites.

## Tests

1. **Whist invariants per size.** For each n ∈ {8, 12, 16, 20, 24}: across rounds 0..n-2, every pair partners exactly once and opposes exactly twice. (May already exist; verify and extend if not.)
2. **Coverage per size.** For each n: assert `maxCoverage` ≤ the value chosen by the search. Capturing the actual value during search; assert it as a regression guard.
3. **Adjacency improvement (n=16, observational).** Count rounds where the same trio of players lands on the same physical court in consecutive rounds. Assert this drops materially compared to the current schedule. (Capture current count as part of the change; assert new count is at most some clearly-smaller bound.)
4. **No regression in existing tests.** `ScheduleDistributionTests` and `ScheduleGeneratorTests` continue to pass without modification (or with mechanical updates to expected matchups if any test pins specific player-IDs to specific rounds).

## Out of scope

- Court adjacency penalty in `ScorePermutation` (was the previous direction; superseded by this structural fix).
- Greedy continuation past round n-1.
- Non-canonical sizes (still go through greedy).
- Bye distribution for odd round-robins.

## Risks and open questions

- **Search may not reach the theoretical floor.** Combinatorial constraints could make the floor unattainable. The metric is "best valid base round we can find," not "provably optimal" — though for small n we may exhaust the space and prove optimality.
- **Existing tests pinning to specific schedules.** If any test asserts exact player-IDs in exact rounds, those will need mechanical updates. Quick scan during implementation will flag them.
- **Interaction with court permutation.** If `maxCoverage` improves but the court labeller still occasionally puts the same trio on the same physical court (due to greedy spread minimization), we may layer the adjacency penalty back on as a small follow-up. Not expected to be needed.
- **Search code maintenance.** The search lives in the repo (test project or a small tool) and is run on demand, not in CI. It should be readable enough to re-run if we later change canonical sizes or constraints.
