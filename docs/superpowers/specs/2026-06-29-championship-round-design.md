# Championship Round — Design

**Date:** 2026-06-29
**Status:** Approved (pending spec review)

## Summary

Add an optional capability to record match results (scores) during a round-robin
event, compute a live individual standings/leaderboard, and generate a final
**championship round** that seeds players by performance: the top-ranked players
play together on court 1, the next group on court 2, and so on.

The feature is zero-config: score inputs are always available on the schedule, and
nothing changes for organizers who don't enter scores.

## Goals

- Optionally record the score of each match (per court, per round).
- Show a live, printable standings table ranking individual players.
- Generate a final "championship" round seeded by standings, with the strongest
  players grouped onto the top court and so on down.
- Leave the existing experience untouched for users who ignore the feature.

## Non-goals

- No bracket/elimination play, multiple final rounds, or best-of-N series.
- No per-game timing, win-by-2 enforcement, or sport-specific score validation.
- No change to how the regular schedule itself is generated.

## Decisions (from brainstorming)

| Question | Decision |
|----------|----------|
| Ranking metric | **Wins → point differential → points for** (record scores). |
| Within-court pairing in the final | **Balanced: seed1 & seed4 vs seed2 & seed3.** |
| Players who don't fill complete courts of 4 | **Lowest seeds sit out** (bye). |
| Surface / workflow | **Always-available inline score inputs on the Schedule page; one "Generate Championship Round" button.** No setup toggle. |
| Standings visibility | **Viewable in-app and included in the printout.** |
| Incomplete scores when generating | **Allow, but warn first** ("X of Y matches unscored"); proceed or cancel. |
| Remove the final round | A **Remove Championship Round** button (regenerating also replaces it). |

## Data model changes

EF Core migration (SQLite). Existing model: `Event → Rounds → Matches` (each match
is 2 teams of 2 on a court) plus `Byes`.

- **`Match`** gains:
  - `int? Team1Score`
  - `int? Team2Score`
  - `null` means "not yet entered". A match counts toward standings only when
    **both** scores are non-null.
- **`Round`** gains:
  - `bool IsChampionship` (default `false`)
  - Marks the final round so it can be (a) replaced on regenerate, (b) excluded
    from standings, and (c) labeled distinctly in the UI.

No new tables. The championship round is an ordinary `Round` row with the flag set.

## Standings — `StandingsCalculator` service

A pure function over the event's **regular rounds only** (rounds where
`IsChampionship == false`). The championship round never feeds standings (it is the
output, not an input).

For each match where **both** `Team1Score` and `Team2Score` are non-null:

- Determine the winning team (higher score). **Equal scores award no win to either
  team** but still contribute points.
- For each of the two players on a team, accumulate:
  - `Wins` += 1 if that player's team won
  - `PointsFor` += own team's score
  - `PointsAgainst` += opposing team's score

A standings row:

```
{
  Player,
  Wins,
  PointsFor,
  PointsAgainst,
  Diff = PointsFor - PointsAgainst
}
```

**Sort order:** `Wins` desc → `Diff` desc → `PointsFor` desc → player name asc
(final deterministic tiebreak).

Players with no scored matches appear with all-zero stats, ordered by the tiebreaks.
Unscored matches simply contribute nothing.

Output: an ordered `List<StandingsRow>` (rank = list position). Used by both the
leaderboard UI and the championship seeding.

## Championship round — builder

Input: the ordered standings and `Event.NumberOfCourts`. Let `N` = number of players
in the standings.

1. `capacity = min(NumberOfCourts, floor(N / 4))` — the number of full courts of 4
   the final round can field.
2. The top `capacity * 4` players play; the rest get **byes** (lowest seeds sit out).
3. For court `k` (1-based), take the next four players in rank order
   `(s1, s2, s3, s4)`:
   - **Team1 = s1 & s4**
   - **Team2 = s2 & s3**
   - `CourtNumber = k`
4. Build a `Round { IsChampionship = true, RoundNumber = (max existing RoundNumber) + 1, Matches = [...], Byes = [...] }`.

Worked example — 14 players, 3 courts: `capacity = min(3, 3) = 3` →
court 1 ranks 1–4, court 2 ranks 5–8, court 3 ranks 9–12, byes ranks 13–14.

The final round is editable like any other round (the existing "Rearrange Players"
within-round swap applies), so organizers can hand-tweak the default pairings.

## Persistence — `EventService` additions

- `SaveMatchScoreAsync(matchId, int? team1Score, int? team2Score)` — persists a
  single match's scores. Called as the organizer enters/edits them.
- `GenerateChampionshipRoundAsync(eventId)`:
  1. Load event with rounds/matches.
  2. Compute standings via `StandingsCalculator` over regular rounds.
  3. **Delete any existing championship round** (and its matches/byes).
  4. Build the new championship round and append it.
  5. Save. Re-running replaces the prior final round.
- `RemoveChampionshipRoundAsync(eventId)` — deletes the championship round (and its
  matches/byes) if present.

`SaveScheduleAsync` (full regenerate) already deletes **all** rounds, so it clears
scores and the championship round as a side effect — acceptable and by design.

## UI — `Schedule.razor`

- **Score inputs:** each match cell renders two small numeric inputs (one per team).
  Always visible (independent of "Rearrange Players" edit mode). Saved on change via
  `SaveMatchScoreAsync`. Inputs display their entered value when printed.
  - Guard against the stale-value pitfall noted in project memory: if Enter/oninput
    handling is used, bind with `@bind:event="oninput"`. Simpler: save on
    `@onchange` (blur/commit).
- **Generate button:** a "Generate Championship Round" button in the existing
  `no-print` toolbar. On click:
  - If any **regular-round** match is missing a score, show a confirm modal:
    *"⚠ X of Y matches unscored. Standings use entered scores only."* with
    **[Generate anyway]** / **[Cancel]**.
  - Otherwise generate immediately.
- **Remove button:** a "Remove Championship Round" button, shown only when a
  championship round exists.
- **Championship row:** renders as the last row of the schedule table, visually
  distinct, labeled **"Championship"** in the Round column instead of a number.
- **Standings table:** rendered below the schedule (inside the printable
  `schedule-sheet`), columns `# / Player / W / Diff / Pts`. Live-updates as scores
  are entered. Included in the printout.
- **Regenerate confirm text:** add a line noting that **entered scores and the
  championship round will be cleared** (it already replaces all rounds).

## Edge cases

- Scores are non-negative integers; blank = unscored; ties award no win but count
  points.
- Zero scored matches: generation still works (all players tied → name order). The
  "unscored" warning covers this case loudly.
- `N < 4` or `capacity == 0`: no full court can be formed → the championship round
  has no matches (all byes). The Generate action should surface this rather than
  create an empty round; show a message ("Not enough players for a championship
  court").
- Existing within-round swap and stats pages operate on the championship round like
  any other round; the Stats page partner/opponent/court matrices will include it
  (acceptable — it is a real round of play).

## Testing

- `StandingsCalculatorTests`: win/loss counting, tie = no win, point diff,
  sort order and tiebreaks, unscored matches ignored, championship round excluded.
- `ChampionshipRoundBuilderTests`: seeding into courts, balanced 1&4 vs 2&3 pairing,
  byes for lowest seeds, capacity limited by courts and by player count, the
  `N < 4` empty case.
- `EventService` tests: save match score round-trips; generate replaces an existing
  championship round; remove deletes it; full regenerate clears scores and final
  round.
```

## Open questions

None outstanding.
