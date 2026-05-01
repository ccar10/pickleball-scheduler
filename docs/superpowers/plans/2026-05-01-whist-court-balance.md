# Court-Balanced Whist Cyclic Schedule Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add hardcoded per-round, per-match court labels to Whist Cyclic schedules so every player's court-visit spread is `≤ 1` across the n-1 round cycle (eliminating the current `(7,0)` stuck-on-one-court problem at 8p/2c).

**Architecture:** A new `CourtLabels` static dictionary in `WhistCyclicSchedule` parallels `BaseRounds`. Each entry maps a Whist size to an array of `n-1` per-round permutations of court indices. `GetRoundMatchups` reads the labels and sets `Match.CourtNumber` directly; `ScheduleGenerator` no longer calls `AssignCourts` for Whist rounds. The labels are discovered by a brute-force search helper (a skipped one-shot test) that mirrors the existing base-round discovery pattern.

**Tech Stack:** .NET 8, xUnit.

**Spec:** `docs/superpowers/specs/2026-05-01-whist-court-balance-design.md`

---

## File Structure

**Modify:**
- `PickleballScheduler/Services/WhistCyclicSchedule.cs` — add `CourtLabels` data; set `CourtNumber` in `GetRoundMatchups`.
- `PickleballScheduler/Services/ScheduleGenerator.cs` — remove the `AssignCourts` call from the Whist branch in `Generate`.
- `PickleballScheduler.Tests/Services/WhistCyclicScheduleTests.cs` — add the court-balance invariant test, plus a `[Theory(Skip="…")]` one-shot search helper that emits paste-ready C# court labels.

No new files.

---

## Task 1: Wh(8) court labels — full TDD cycle including the generator helper

**Files:**
- Modify: `PickleballScheduler/Services/WhistCyclicSchedule.cs`
- Modify: `PickleballScheduler/Services/ScheduleGenerator.cs`
- Modify: `PickleballScheduler.Tests/Services/WhistCyclicScheduleTests.cs`

This task covers everything needed for Wh(8): a court-balance invariant test (initially red), a brute-force generator helper, hardcoded Wh(8) labels, and wiring `GetRoundMatchups` to use them. After this task, the 8-player case is provably balanced (max-min ≤ 1 court visits per player); other sizes are not yet covered by the invariant test.

### Step 1: Add the court-balance invariant test (Wh(8) only)

In `PickleballScheduler.Tests/Services/WhistCyclicScheduleTests.cs`, append a new test:

```csharp
[Theory]
[InlineData(8)]
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
            Assert.InRange(courtIdx, 0, courts - 1);
            foreach (var pid in new[] { m.Team1Player1Id, m.Team1Player2Id, m.Team2Player1Id, m.Team2Player2Id })
                visits[pid][courtIdx]++;
        }
    }

    foreach (var (pid, counts) in visits)
    {
        var max = counts.Max();
        var min = counts.Min();
        Assert.True(max - min <= 1,
            $"Player {pid} court visits [{string.Join(",", counts)}] — spread {max - min} > 1");
    }
}
```

### Step 2: Run the test — expect FAIL

```
dotnet test PickleballScheduler.Tests/PickleballScheduler.Tests.csproj --filter "FullyQualifiedName~EachPlayerCourtVisitsAreBalanced"
```

Expected: FAIL. Currently `GetRoundMatchups` does not set `CourtNumber` (it's set later by `AssignCourts`), so `courtIdx` will be `-1`, the `Assert.InRange` will fail, and the message will tell you the player and counts. If for some reason it doesn't FAIL on `InRange`, it'll fail on the spread assertion — both are evidence the invariant doesn't hold.

### Step 3: Add the one-shot court-label search helper

Append to the same test file (after the invariant test, before the closing `}`):

```csharp
[Theory(Skip = "One-shot generator. Remove Skip locally to emit court labels for the requested size; copy output into WhistCyclicSchedule.CourtLabels.")]
[InlineData(8)]
[InlineData(12)]
[InlineData(16)]
[InlineData(20)]
[InlineData(24)]
public void GenerateCourtLabels(int playerCount)
{
    var players = Enumerable.Range(1, playerCount)
        .Select(id => new Player { Id = id, Name = $"P{id}" })
        .ToList();
    var courts = playerCount / 4;
    var rounds = playerCount - 1;
    var ceiling = (rounds + courts - 1) / courts;  // ceil(rounds / courts)

    // visits[pid][courtIdx] — running counts across the schedule under construction
    var visits = players.ToDictionary(p => p.Id, _ => new int[courts]);

    // Cache each round's matchups (deterministic from BaseRounds + rotation).
    var roundMatchups = new List<List<Match>>(rounds);
    for (int r = 0; r < rounds; r++)
        roundMatchups.Add(WhistCyclicSchedule.GetRoundMatchups(players, r));

    var labels = new int[rounds][];
    var perms = AllPermutations(courts).ToList();
    var found = TrySearch(0, roundMatchups, perms, visits, ceiling, labels);

    if (!found)
    {
        Assert.Fail($"No court labeling found for n={playerCount} that keeps every player's spread <= 1.");
        return;
    }

    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"// Wh({playerCount}) court labels — every player's spread <= 1 across {rounds} rounds.");
    sb.Append($"[{playerCount}] = new int[][] {{ ");
    for (int r = 0; r < rounds; r++)
    {
        sb.Append("new int[] { ");
        sb.Append(string.Join(", ", labels[r]));
        sb.Append(" }");
        if (r < rounds - 1) sb.Append(", ");
    }
    sb.Append(" },");

    Assert.Fail(sb.ToString());
}

private static bool TrySearch(int round, List<List<Match>> matchups, List<int[]> perms,
    Dictionary<int, int[]> visits, int ceiling, int[][] labels)
{
    if (round == matchups.Count)
    {
        foreach (var counts in visits.Values)
            if (counts.Max() - counts.Min() > 1) return false;
        return true;
    }

    var matches = matchups[round];

    // Heuristic: try permutations in an order that lifts the most-disadvantaged player first.
    foreach (var perm in OrderPermutations(perms, matches, visits))
    {
        bool ok = true;
        // Apply
        for (int i = 0; i < matches.Count && ok; i++)
        {
            var ci = perm[i];
            foreach (var pid in PlayerIds(matches[i]))
            {
                visits[pid][ci]++;
                if (visits[pid][ci] > ceiling) ok = false;
            }
        }

        if (ok)
        {
            labels[round] = (int[])perm.Clone();
            if (TrySearch(round + 1, matchups, perms, visits, ceiling, labels)) return true;
        }

        // Undo (whether we partially applied or not)
        for (int i = 0; i < matches.Count; i++)
        {
            var ci = perm[i];
            foreach (var pid in PlayerIds(matches[i]))
                if (visits[pid][ci] > 0) visits[pid][ci]--;
        }
    }

    return false;
}

private static IEnumerable<int> PlayerIds(Match m)
{
    yield return m.Team1Player1Id;
    yield return m.Team1Player2Id;
    yield return m.Team2Player1Id;
    yield return m.Team2Player2Id;
}

private static IEnumerable<int[]> AllPermutations(int n)
{
    var arr = Enumerable.Range(0, n).ToArray();
    return PermuteRecursive(arr, 0);
}

private static IEnumerable<int[]> PermuteRecursive(int[] arr, int start)
{
    if (start == arr.Length - 1)
    {
        yield return (int[])arr.Clone();
        yield break;
    }
    for (int i = start; i < arr.Length; i++)
    {
        (arr[start], arr[i]) = (arr[i], arr[start]);
        foreach (var p in PermuteRecursive(arr, start + 1)) yield return p;
        (arr[start], arr[i]) = (arr[i], arr[start]);
    }
}

private static IEnumerable<int[]> OrderPermutations(List<int[]> perms, List<Match> matches, Dictionary<int, int[]> visits)
{
    // Score = sum over (match i, player in match i) of visits[player][perm[i]]. Lower is better.
    return perms
        .Select(perm => (perm, score: ScorePerm(perm, matches, visits)))
        .OrderBy(t => t.score)
        .Select(t => t.perm);
}

private static int ScorePerm(int[] perm, List<Match> matches, Dictionary<int, int[]> visits)
{
    int s = 0;
    for (int i = 0; i < matches.Count; i++)
        foreach (var pid in PlayerIds(matches[i]))
            s += visits[pid][perm[i]];
    return s;
}
```

Note: this generator depends on `GetRoundMatchups` returning matchups regardless of whether court labels exist yet. Today it doesn't set `CourtNumber`, so it works fine. After Step 5 it will set `CourtNumber` from `CourtLabels`, but the generator only reads team player IDs from each match — it does not read `CourtNumber` — so it remains correct.

### Step 4: Run the generator for Wh(8)

Temporarily remove the `Skip = ...` from the `[Theory]` (or comment it) and run only the size-8 case:

```
dotnet test PickleballScheduler.Tests/PickleballScheduler.Tests.csproj --filter "FullyQualifiedName~GenerateCourtLabels(playerCount: 8)"
```

The test will fail intentionally via `Assert.Fail`, with output like:

```
// Wh(8) court labels — every player's spread <= 1 across 7 rounds.
[8] = new int[][] { new int[] { 0, 1 }, new int[] { 1, 0 }, new int[] { 0, 1 }, new int[] { 1, 0 }, new int[] { 0, 1 }, new int[] { 1, 0 }, new int[] { 0, 1 } },
```

(The exact integers may differ — what matters is the pattern.)

Copy the printed line — you'll paste it in Step 5. Re-add the `Skip` attribute when done.

If the generator reports "No court labeling found for n=8 that keeps every player's spread <= 1," see the troubleshooting note at the end of this task.

### Step 5: Add the `CourtLabels` dictionary with the Wh(8) entry

In `PickleballScheduler/Services/WhistCyclicSchedule.cs`, just below the `BaseRounds` declaration, add:

```csharp
// Per-round, per-match court index assignments. CourtLabels[n][r][i] = court index for matches[i] in round r
// of the n-player schedule. Generated by the WhistCyclicScheduleTests.GenerateCourtLabels one-shot search
// and verified by EachPlayerCourtVisitsAreBalanced (max-min <= 1 court visits per player).
private static readonly IReadOnlyDictionary<int, int[][]> CourtLabels =
    new Dictionary<int, int[][]>
    {
        // Paste the Wh(8) entry from Step 4 here, e.g.:
        // [8] = new int[][] { new int[] { 0, 1 }, new int[] { 1, 0 }, ..., new int[] { 0, 1 } },
    };
```

Replace the placeholder comment with the actual line emitted by the generator in Step 4.

### Step 6: Set `CourtNumber` in `GetRoundMatchups`

In `WhistCyclicSchedule.cs`, modify `GetRoundMatchups`. Find the existing return path:

```csharp
        var matches = new List<Match>(baseRound.Length);
        foreach (var bm in baseRound)
        {
            matches.Add(new Match
            {
                Team1Player1Id = ResolveRole(bm.A, roundIndex, rotateMod, players),
                Team1Player2Id = ResolveRole(bm.B, roundIndex, rotateMod, players),
                Team2Player1Id = ResolveRole(bm.C, roundIndex, rotateMod, players),
                Team2Player2Id = ResolveRole(bm.D, roundIndex, rotateMod, players),
            });
        }
        return matches;
    }
```

Replace it with:

```csharp
        var matches = new List<Match>(baseRound.Length);
        foreach (var bm in baseRound)
        {
            matches.Add(new Match
            {
                Team1Player1Id = ResolveRole(bm.A, roundIndex, rotateMod, players),
                Team1Player2Id = ResolveRole(bm.B, roundIndex, rotateMod, players),
                Team2Player1Id = ResolveRole(bm.C, roundIndex, rotateMod, players),
                Team2Player2Id = ResolveRole(bm.D, roundIndex, rotateMod, players),
            });
        }

        // Apply the court labels if we have them for this size; sizes without labels keep
        // CourtNumber = 0 and rely on the caller to assign courts (current behavior for
        // sizes added in Task 2).
        if (CourtLabels.TryGetValue(players.Count, out var schedule))
        {
            var labels = schedule[roundIndex];
            for (int i = 0; i < matches.Count; i++)
                matches[i].CourtNumber = labels[i] + 1;
        }

        return matches;
    }
```

### Step 7: Skip `AssignCourts` for Whist rounds in `ScheduleGenerator.Generate`

In `PickleballScheduler/Services/ScheduleGenerator.cs`, find the Whist branch (around the `if (useWhist)` block). It currently does:

```csharp
        if (useWhist)
        {
            matches = WhistCyclicSchedule.GetRoundMatchups(players, r);
            AssignCourts(matches, courtCounts, matchesPerRound);
        }
```

Change to:

```csharp
        if (useWhist)
        {
            matches = WhistCyclicSchedule.GetRoundMatchups(players, r);
            // GetRoundMatchups sets CourtNumber when court labels exist for this size.
            // For sizes without labels yet (added in Task 2), fall back to AssignCourts.
            if (matches.Count > 0 && matches[0].CourtNumber == 0)
                AssignCourts(matches, courtCounts, matchesPerRound);
        }
```

### Step 8: Run the invariant test — expect PASS for Wh(8)

```
dotnet test PickleballScheduler.Tests/PickleballScheduler.Tests.csproj --filter "FullyQualifiedName~EachPlayerCourtVisitsAreBalanced"
```

Expected: PASS for size 8. (The theory only has `[InlineData(8)]` at this point.)

### Step 9: Confirm the full non-distribution suite

```
dotnet test PickleballScheduler.Tests/PickleballScheduler.Tests.csproj --filter "FullyQualifiedName!~ScheduleDistributionTests" --nologo --verbosity quiet 2>&1 | tail -5
```

Expected: 53 passing (52 prior + 1 new invariant case for size 8), 2 skipped (existing `GenerateBaseRound` + new `GenerateCourtLabels`), 0 failing.

The pre-existing `Generate_8Players_2Courts_7Rounds_PerfectWhistCycle` smoke test must still pass — Whist invariants (partner-once / opponent-twice) are unaffected by court labels.

### Step 10: Commit

```
git add PickleballScheduler/Services/WhistCyclicSchedule.cs \
  PickleballScheduler/Services/ScheduleGenerator.cs \
  PickleballScheduler.Tests/Services/WhistCyclicScheduleTests.cs
git commit -m "feat: court-balanced labels for Wh(8); other sizes unchanged"
```

(HEREDOC if needed; no `--no-verify` / `--amend`.)

### Troubleshooting (only if the generator fails for Wh(8))

If the generator at Step 4 reports "No court labeling found", the existing Wh(8) base round genuinely doesn't admit a balanced labeling and we need to regenerate it jointly with court labels. Steps:

1. Open `WhistCyclicSchedule.cs` and look at the existing `BaseRounds[8]` entry.
2. In the test file, write a slightly larger one-shot search that takes a candidate base round, runs the existing invariant search to find a Wh(8) base round (mirroring the existing `BruteForceBaseRound` helper), and then feeds the result into the court-labels search. Accept the first `(base round, court labels)` pair.
3. Update both `BaseRounds[8]` and `CourtLabels[8]` in `WhistCyclicSchedule.cs`.
4. Re-run all Whist tests — `AllPairsPartnerOnceAndOpposeTwice` and `EachPlayerCourtVisitsAreBalanced` must both pass for size 8.

This is unlikely (the brute-force search has 128 raw candidates with strong pruning, and balanced labelings exist for any well-formed Wh(8)). But the joint-search fallback is documented in the spec and available if needed.

---

## Task 2: Wh(12), Wh(16), Wh(20), Wh(24) court labels

**Files:**
- Modify: `PickleballScheduler/Services/WhistCyclicSchedule.cs`
- Modify: `PickleballScheduler.Tests/Services/WhistCyclicScheduleTests.cs`

Adds the remaining four court-label entries and extends the invariant test to cover all 5 sizes. The generator helper from Task 1 is unchanged — just used for new sizes.

### Step 1: Generate Wh(12) labels

Temporarily remove the `Skip = ...` from `GenerateCourtLabels` and run for size 12:

```
dotnet test PickleballScheduler.Tests/PickleballScheduler.Tests.csproj --filter "FullyQualifiedName~GenerateCourtLabels(playerCount: 12)"
```

Expected runtime: seconds to a few minutes. Capture the emitted `[12] = new int[][] { … }` line.

If the generator reports "No court labeling found", apply the joint-search fallback from Task 1's troubleshooting section (regenerate the Wh(12) base round + labels together, replacing both `BaseRounds[12]` and adding `CourtLabels[12]`).

### Step 2: Generate Wh(16) labels

Run for size 16:

```
dotnet test PickleballScheduler.Tests/PickleballScheduler.Tests.csproj --filter "FullyQualifiedName~GenerateCourtLabels(playerCount: 16)"
```

Expected runtime: seconds to ~30 minutes depending on pruning. Capture the line.

### Step 3: Generate Wh(20) labels

Run for size 20:

```
dotnet test PickleballScheduler.Tests/PickleballScheduler.Tests.csproj --filter "FullyQualifiedName~GenerateCourtLabels(playerCount: 20)"
```

Expected runtime: minutes to hours. Capture the line. If runtime exceeds 1 hour, accept `spread ≤ 2` for this size and note it in Step 6's invariant assertion.

### Step 4: Generate Wh(24) labels

Run for size 24:

```
dotnet test PickleballScheduler.Tests/PickleballScheduler.Tests.csproj --filter "FullyQualifiedName~GenerateCourtLabels(playerCount: 24)"
```

Expected runtime: hours. Capture the line. Same `spread ≤ 2` exception applies if the search runs out of budget.

### Step 5: Add the four entries to `CourtLabels`

In `PickleballScheduler/Services/WhistCyclicSchedule.cs`, expand the `CourtLabels` dictionary to include the four new entries alongside the Wh(8) entry from Task 1. Each entry is the `[N] = new int[][] { … }` line emitted by the generator.

After this step, `CourtLabels` has all five entries: 8, 12, 16, 20, 24.

### Step 6: Extend the invariant test theory

In `PickleballScheduler.Tests/Services/WhistCyclicScheduleTests.cs`, change:

```csharp
[Theory]
[InlineData(8)]
public void EachPlayerCourtVisitsAreBalanced(int playerCount)
```

to:

```csharp
[Theory]
[InlineData(8)]
[InlineData(12)]
[InlineData(16)]
[InlineData(20)]
[InlineData(24)]
public void EachPlayerCourtVisitsAreBalanced(int playerCount)
```

If any size accepted the `spread ≤ 2` exception (per spec Goal #1 / Q1 answer B), change the `Assert.True(max - min <= 1, …)` line to:

```csharp
var allowed = playerCount switch
{
    20 => 2,    // List sizes that needed the exception, with comment naming why
    24 => 2,
    _ => 1,
};
Assert.True(max - min <= allowed,
    $"Player {pid} court visits [{string.Join(",", counts)}] — spread {max - min} > {allowed}");
```

Skip this customization if every size made the `≤ 1` bar.

### Step 7: Run all Whist tests

```
dotnet test PickleballScheduler.Tests/PickleballScheduler.Tests.csproj --filter "FullyQualifiedName~WhistCyclicScheduleTests"
```

Expected: 5 invariant cases pass for `AllPairsPartnerOnceAndOpposeTwice`, 5 invariant cases pass for `EachPlayerCourtVisitsAreBalanced`, plus the `IsSupportedSize` cases. 2 generators stay skipped.

### Step 8: Confirm the full non-distribution suite

```
dotnet test PickleballScheduler.Tests/PickleballScheduler.Tests.csproj --filter "FullyQualifiedName!~ScheduleDistributionTests" --nologo --verbosity quiet 2>&1 | tail -5
```

Expected: 57 passing (53 from Task 1 baseline + 4 new invariant cases for sizes 12, 16, 20, 24), 2 skipped, 0 failing.

### Step 9: Commit

```
git add PickleballScheduler/Services/WhistCyclicSchedule.cs \
  PickleballScheduler.Tests/Services/WhistCyclicScheduleTests.cs
git commit -m "feat: court-balanced labels for Wh(12), Wh(16), Wh(20), Wh(24)"
```

(HEREDOC if needed; no `--no-verify` / `--amend`.)

---

## Self-Review Notes

- **Spec coverage:**
  - Goal 1 (every player's spread ≤ 1 for all 5 sizes, with documented `≤ 2` exceptions if needed) → Tasks 1 & 2 invariant test, with conditional threshold in Task 2 Step 6.
  - Goal 2 (data-driven, hardcoded labels, transcribed from one-shot search) → Tasks 1 & 2 use the `GenerateCourtLabels` helper and paste its output into source.
  - Goal 3 (build-time verification) → `EachPlayerCourtVisitsAreBalanced` runs in CI and fails specifically when a transcribed value is wrong.
  - Goal 4 (non-Whist paths unchanged) → Task 1 Step 7 only modifies the `useWhist` branch; the `else` (joint search) path is untouched.
  - Architecture (`CourtLabels` parallel to `BaseRounds`) → Task 1 Step 5.
  - `GetRoundMatchups` change → Task 1 Step 6.
  - `ScheduleGenerator` change → Task 1 Step 7.
  - Search algorithm (per-round permutations + branch-and-bound prune at ceiling) → Task 1 Step 3 (`TrySearch`).
  - Joint-search fallback → Task 1 troubleshooting and Task 2 Step 1 note.

- **Type consistency:**
  - `CourtLabels` is `IReadOnlyDictionary<int, int[][]>` — used uniformly in both tasks.
  - `EachPlayerCourtVisitsAreBalanced(int playerCount)` signature stays identical between Task 1 (single InlineData) and Task 2 (extended).
  - `GenerateCourtLabels(int playerCount)` is added in Task 1 with `[Theory(Skip=…)]`; Task 2 only un-skips locally to run, never modifies the signature.
  - `Match.CourtNumber` is `int` and 1-based throughout the codebase — Task 1 Step 6 multiplies by `+1` to convert from 0-based label to 1-based number, consistent with how `AssignCourts` already does it.
