# Pairing Algorithm Redesign

**Date:** 2026-04-30
**Status:** Draft (awaiting review)

## Background

The current `ScheduleGenerator` uses a two-pass per-round backtracking algorithm:

1. **Form teams** — minimize `Σ partnerCount²`, with a `+1000` penalty for partnering the same pair in the immediately previous round.
2. **Pair teams into matches** — greedy backtracking that minimizes the *linear* sum of opponent counts. No back-to-back-opponent penalty. Has dead code attempting to address a "Stephen/Dave problem" (two players that always end up on the same side, never opposing).

End-user feedback (Paul Verrier, 2026-04-17 and 2026-04-30):

- Opponents need to be spread out more.
- Limit how often the same people play back-to-back with each other.

The root cause is that **partner selection and opponent selection are decoupled passes.** Optimal partners can lock in opponent groupings the second pass can't undo. Compounding this, opponent cost is linear (not quadratic), so spreading is weak, and there is no consecutive-round penalty for opponents.

## Goals

1. Eliminate forced partner repeats while any pair containing one of the players hasn't been used yet.
2. Eliminate consecutive-round opponent matchups whenever the active pool is large enough to allow it.
3. Spread partners and opponents with equal weight when both rules are satisfied.
4. When configuration makes the rules infeasible, surface this to the user via a banner with a suggested better configuration.
5. Add a distribution test that exercises many random configurations and asserts on fairness metrics, so future algorithm changes can be validated.

## Non-Goals

- Bye-tiebreaker fairness changes (player ID still breaks ties; deferred).
- Mixed Doubles / Fixed Partners formats (separate brainstorm).
- Whole-schedule local search / multi-restart optimization (alternative not chosen).

## Hard Rules

### HR1 — No Early Partner Repeats

A pair `(a,b)` is allowed to partner in round `r` only if every other pair containing `a` and every other pair containing `b` has already partnered at least as many times. Operationally, when scoring, count an HR1 violation when the pair's prior partner count is **strictly greater than the minimum prior partner count among all pairs sharing a player with it**. This is per-player, so it works correctly when not all players are active in every round.

### HR2 — No Consecutive Opponents

A pair `(a,b)` is allowed to oppose in round `r` only if they did not oppose in round `r−1`. Symmetric, looks back exactly one round.

## Cost Function

Each round is scored with a five-element tuple, compared lexicographically (smaller wins):

| Level | Term | Notes |
|-------|------|-------|
| 1 | `hr1Violations` (this round) | Forced early partner repeats |
| 2 | `hr2Violations` (this round) | Consecutive-round opponent pairs |
| 3 | `Σ partnerCount²` after applying this round | Quadratic — penalizes whichever side is currently more lopsided |
| 4 | `Σ opponentCount²` after applying this round | Upgrade from current linear sum |
| 5 | Court imbalance | Same as today |

**Rationale for HR1 above HR2:**

- HR1 protects the fundamental round-robin promise: did every player partner with every other player?
- HR2 is structurally infeasible at very small active pools (4 active = only 2 possible opponents). If the levels were summed, the algorithm would happily trade away an HR1 violation to avoid an unavoidable HR2 violation. Lex ordering keeps HR1 clean whenever possible.
- The originating user complaint was a partner repeat, not an opponent repeat.

**Rationale for quadratic spread on levels 3 and 4:**

- Quadratic cost naturally penalizes whichever side (partners or opponents) is currently more lopsided, giving balanced spread without an explicit weight knob.
- Level-3 and level-4 are weighted equally by virtue of being the same shape; the user requested both spread objectives carry equal importance.

## Algorithm

### Per-round flow

1. **Bye selection** — unchanged. Players ordered by descending bye count, ties broken by player ID; first `playersPerRound` are active.

2. **Circle-method short-circuit** — unchanged behavior, refined scope. When the player count is even and every player plays every round, the circle method produces a true 1-factorization for the first `n−1` rounds, satisfying HR1 perfectly. We continue to use it for partner selection in those rounds. The new joint-search optimizer then runs *only over the opponent-split layer* (assigning teams to matches and which two teams oppose), since partner pairs are fixed by the circle.

3. **Joint search (full search path)** — replaces the existing two-pass partner-then-opponent selection. Used in:
   - Rounds where the circle method does not apply (odd counts, byes-needed cases).
   - Rounds past the circle's `n−1` limit.

### Joint search structure

For each round, enumerate all partitions of the `4k` active players into `k` matches, where each match is an unordered pair of teams of two. Score each candidate with the lex tuple. Backtrack with pruning.

Concrete recursion:

- Pick the lowest-indexed unused player `p`.
- Choose `p`'s partner `q` from the unused set.
- Pick the next lowest-indexed unused player `p'`.
- Choose `p'`'s partner `q'`. The match `{(p,q), (p',q')}` is now formed.
- Recurse for the next match.
- Within a match the two teams are unordered and the players within each team are unordered, so candidates are not double-counted.

**Pruning:** Every level of the cost tuple is monotone non-decreasing as more matches are added (each term is a sum of non-negative contributions). When the running tuple lex-exceeds the current best complete tuple, prune.

**Search-size sanity:**

For `2k` active players forming `k` matches, the number of round configurations is `(2k)! / (4!^k · k!) · 3^k` — the partitions of `2k` players into `k` unordered groups of four, times the 3 ways to split each group of four into two unordered teams.

| Active players | Round configurations | Notes |
|----------------|----------------------|-------|
| 4 | 3 | trivial |
| 8 | 315 | trivial |
| 12 | ~156,000 | fast |
| 16 | ~213,000,000 | large; pruning is critical |

Paul's typical case (8 players, 2 courts) is trivial. The 16-active case (e.g., 4 courts, all play) requires the lex-tuple pruning to be effective. Because every cost level is monotone non-decreasing as matches are added, partial configurations that already lex-exceed the running best are skipped — in practice this collapses the tree by orders of magnitude. The plan must include a benchmark to confirm sub-second generation at 16 active players; if pruning proves insufficient, fall back to a beam-search variant (keep the top-`B` partial configurations at each depth).

## Result Type

`ScheduleGenerator.Generate(...)` changes its return type:

```csharp
public record ScheduleResult(
    List<Round> Rounds,
    int Hr1Violations,   // total count of forced early partner repeats across all rounds
    int Hr2Violations);  // total count of consecutive-opponent pairs across all rounds
```

All call sites update to read `result.Rounds`.

## Banner

The schedule renders on `Schedule.razor` after `EventSetup.razor` saves it, then navigates away. The banner therefore needs to read its data from the persisted event, not from generator return values that only exist at creation time.

### Persistence

Add two `int` columns to the `Event` entity: `Hr1Violations` and `Hr2Violations`. EF Core migration adds them to the `Events` table with default `0` for any pre-existing rows. `EventService.SaveScheduleAsync` (or its caller in `EventSetup.razor`) writes the counts from `ScheduleResult` onto the event before saving.

### Display

Shown on `Schedule.razor`, above the schedule table, when either persisted count is non-zero. Not rendered on the print view.

> ⚠ With these settings, this schedule has some unavoidable repeats: **N** early partner repeat(s), **M** back-to-back opponent matchup(s). For a perfectly fair schedule, try **{suggestion}**.

`{suggestion}` is computed once at generation time by re-running the generator with cheap configuration variations (round count and player count adjusted by ±1 and ±2, courts unchanged) and picking the smallest change that yields zero violations. If no near-neighbor configuration works, the suggestion clause is dropped and only the warning is shown. The chosen suggestion is persisted as a third nullable column `Event.RepeatSuggestion` (string, nullable) so `Schedule.razor` can render it without re-running the generator.

## Distribution Test

New file: `PickleballScheduler.Tests/Services/ScheduleDistributionTests.cs`.

### Theory test

A `[Theory]` driven by ~100 fixed `(playerCount, courtCount, roundCount, seed)` tuples generated once at compile time from a master seed and baked into the test data, so failures are reproducible.

Coverage envelope per case:

- `playerCount`: 4–24
- `courtCount`: 1–6
- `roundCount`: 3–15

Each case generates a schedule, computes metrics, and asserts:

| Metric | Threshold |
|--------|-----------|
| `hr1Violations` | == 0 when feasible; ≤ `ExpectedMinForcedRepeats(playerCount, roundCount, activePool)` otherwise |
| `hr2Violations` | == 0 when active pool ≥ 6; ≤ theoretical-minimum otherwise |
| `max(partnerCount) − min(partnerCount)` | ≤ 1 |
| `max(opponentCount) − min(opponentCount)` | ≤ 2 |
| Bye fairness `max−min byes per player` | ≤ 1 |
| Per-player court fairness `max−min court visits` | ≤ 2 |
| Coverage: every player pair has partnered | true when `roundCount ≥ playerCount−1` and all play |

### Feasibility helpers (test-project static methods)

- `int ExpectedMinForcedRepeats(int playerCount, int rounds, int activePool)` — given the active-pool size and round count, how many partner-repeats are unavoidable.
- `bool Hr2Feasible(int playerCount, int courtCount)` — `false` when `playerCount == 4 && courtCount == 1` (the same 4 players are always active and always face each other); otherwise `true`. With 6+ total players or 2+ courts, bye rotation or court structure gives the algorithm enough room to break consecutive-opponent chains.

### Stress test

A single `[Fact]` runs 1000 random configurations and asserts that core invariants always hold:

- No player is double-booked in a round.
- Every active player appears in exactly one match.
- Court numbers are within `[1, courtCount]`.
- No exceptions thrown.

This catches structural bugs the theory test might miss.

## Files Changing

- `PickleballScheduler/Services/ScheduleGenerator.cs` — replace two-pass team/match selection with the joint search; add HR1/HR2 counters; change return type to `ScheduleResult`.
- `PickleballScheduler/Services/ScheduleResult.cs` — new record.
- `PickleballScheduler/Models/Event.cs` — add `Hr1Violations` (int), `Hr2Violations` (int), `RepeatSuggestion` (string?, nullable).
- `PickleballScheduler/Migrations/...` — new EF Core migration adding the three columns to `Events` with default `0` / `null`.
- `PickleballScheduler/Services/EventService.cs` — `SaveScheduleAsync` (or a new overload) accepts the violation counts and suggestion, writes them onto the event.
- `PickleballScheduler/Components/Pages/EventSetup.razor` — consume `ScheduleResult`, compute the suggestion via near-neighbor re-runs, pass everything to `EventService.SaveScheduleAsync`.
- `PickleballScheduler/Components/Pages/Schedule.razor` — render the banner when `evt.Hr1Violations > 0 || evt.Hr2Violations > 0`.
- `PickleballScheduler.Tests/Services/ScheduleGeneratorTests.cs` — update to read `result.Rounds`.
- `PickleballScheduler.Tests/Services/ScheduleDistributionTests.cs` — new (theory + stress).

## Testing Strategy

- All existing `ScheduleGeneratorTests` continue to pass after the API shape change.
- The new `ScheduleDistributionTests` provides ongoing fairness regression coverage.
- Manual verification on Paul's representative case (8 players, 2 courts, 10 rounds): zero violations, balanced partner/opponent matrices.

## Open Questions Resolved During Brainstorm

- **Priority of partner vs opponent spread when soft objectives conflict:** equal weight, both quadratic.
- **Hard rules vs soft preferences:** hard rules HR1 and HR2, with HR1 strictly above HR2 in lex order.
- **Behavior when hard rules infeasible:** best-effort with banner suggesting a better configuration.
- **Joint vs decoupled selection:** joint search per round, replacing the current two-pass.
- **Algorithm-style alternative chosen:** Approach 1 (joint round optimization with hierarchical cost), not Approach 2 (cost-tweak only) or Approach 3 (whole-schedule local search).
