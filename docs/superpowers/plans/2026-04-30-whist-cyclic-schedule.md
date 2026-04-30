# Whist Cyclic Schedule Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Use a perfect Whist Cyclic schedule (every pair partners once, every pair opposes exactly twice) for the first `n-1` rounds whenever the configuration qualifies (`playerCount ∈ {8, 12, 16, 20, 24}`, `courts ≥ playerCount/4`, `rounds ≥ playerCount-1`); fall back to the existing joint search for everything else.

**Architecture:** A new `WhistCyclicSchedule` static helper holds the 5 transcribed base rounds and a rotation function. `ScheduleGenerator.Generate` adds a per-round branch that calls Whist when applicable, otherwise uses the existing `BuildRound` joint search. A unit-test verifies each base round against the Whist invariants — partner-once, opponent-twice — so transcription bugs are caught at build time.

**Tech Stack:** .NET 8, xUnit.

**Spec:** `docs/superpowers/specs/2026-04-30-whist-cyclic-schedule-design.md`

---

## File Structure

**Create:**
- `PickleballScheduler/Services/WhistCyclicSchedule.cs` — `IsSupportedSize`, `GetRoundMatchups`, base-round data, rotation helper.
- `PickleballScheduler.Tests/Services/WhistCyclicScheduleTests.cs` — Whist invariant test.

**Modify:**
- `PickleballScheduler/Services/ScheduleGenerator.cs` — add the per-round branch that calls into Whist when the configuration qualifies.
- `PickleballScheduler.Tests/Services/ScheduleGeneratorTests.cs` — add Paul's-case integration test plus a fallback test for an unsupported size.

---

## Task 1: Create `WhistCyclicSchedule` skeleton with `IsSupportedSize`

**Files:**
- Create: `PickleballScheduler/Services/WhistCyclicSchedule.cs`
- Create: `PickleballScheduler.Tests/Services/WhistCyclicScheduleTests.cs`

This task adds the file and the simple `IsSupportedSize` predicate. `GetRoundMatchups` is stubbed to throw `NotImplementedException` until Task 2.

- [ ] **Step 1: Write tests for `IsSupportedSize`**

Create `PickleballScheduler.Tests/Services/WhistCyclicScheduleTests.cs`:

```csharp
using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

public class WhistCyclicScheduleTests
{
    [Theory]
    [InlineData(8, true)]
    [InlineData(12, true)]
    [InlineData(16, true)]
    [InlineData(20, true)]
    [InlineData(24, true)]
    [InlineData(4, false)]
    [InlineData(10, false)]
    [InlineData(28, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void IsSupportedSize_KnownCases(int playerCount, bool expected)
    {
        Assert.Equal(expected, WhistCyclicSchedule.IsSupportedSize(playerCount));
    }
}
```

- [ ] **Step 2: Run — expect compile failure**

```
dotnet test PickleballScheduler.Tests/PickleballScheduler.Tests.csproj --filter "FullyQualifiedName~WhistCyclicScheduleTests"
```

- [ ] **Step 3: Create the helper class with `IsSupportedSize` and a stubbed `GetRoundMatchups`**

Create `PickleballScheduler/Services/WhistCyclicSchedule.cs`:

```csharp
using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

/// <summary>
/// Whist Cyclic schedule generator. For supported sizes (n = 8, 12, 16, 20, 24),
/// produces the first n-1 rounds of a Whist tournament: every pair partners
/// exactly once and every pair opposes exactly twice.
/// </summary>
internal static class WhistCyclicSchedule
{
    private static readonly HashSet<int> SupportedSizes = new() { 8, 12, 16, 20, 24 };

    public static bool IsSupportedSize(int playerCount) => SupportedSizes.Contains(playerCount);

    public static List<Match> GetRoundMatchups(List<Player> players, int roundIndex)
    {
        throw new NotImplementedException("GetRoundMatchups is implemented in Task 2.");
    }
}
```

- [ ] **Step 4: Run — expect 10 PASS**

```
dotnet test PickleballScheduler.Tests/PickleballScheduler.Tests.csproj --filter "FullyQualifiedName~WhistCyclicScheduleTests"
```

Expected: 10/10 pass.

- [ ] **Step 5: Confirm full suite**

```
dotnet test PickleballScheduler.Tests/PickleballScheduler.Tests.csproj --filter "FullyQualifiedName!~ScheduleDistributionTests" --nologo --verbosity quiet 2>&1 | tail -5
```

Expected: 45 passing (35 prior + 10 new), 0 failing.

- [ ] **Step 6: Commit**

```
git add PickleballScheduler/Services/WhistCyclicSchedule.cs \
  PickleballScheduler.Tests/Services/WhistCyclicScheduleTests.cs
git commit -m "feat: add WhistCyclicSchedule.IsSupportedSize"
```

---

## Task 2: Implement Wh(8) base round + rotation + invariant test

**Files:**
- Modify: `PickleballScheduler/Services/WhistCyclicSchedule.cs`
- Modify: `PickleballScheduler.Tests/Services/WhistCyclicScheduleTests.cs`

This task builds the data model (`BaseMatch`), implements rotation, and adds the invariant test for Wh(8). The other sizes follow in Task 3.

### Background — base round semantics

Players are labeled `∞, 0, 1, ..., n-2`. `players[0]` is `∞`; `players[1]` is index `0`; ... `players[n-1]` is index `n-2`.

A `BaseMatch` declares two teams, each with two role labels. A role label is `"inf"` for `∞`, otherwise the string form of an integer in `[0, n-2]`.

For round `r`, each integer role `i` rotates to `(i + r) mod (n-1)`; the `"inf"` role does not rotate. The resolved index `i'` maps to `players[1 + i']`; `"inf"` maps to `players[0]`.

### Wh(8) base round

The candidate base round below comes from a standard Whist Cyclic construction. The invariant test in Step 4 verifies it; if the test fails, try the alternate listed under "Notes" at the bottom of this task.

Match 1: `(∞, 0)` vs `(1, 3)`
Match 2: `(2, 6)` vs `(4, 5)`

- [ ] **Step 1: Add the Wh(8) data and rotation helpers**

Replace the body of `PickleballScheduler/Services/WhistCyclicSchedule.cs` with:

```csharp
using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

internal static class WhistCyclicSchedule
{
    private static readonly HashSet<int> SupportedSizes = new() { 8, 12, 16, 20, 24 };

    public static bool IsSupportedSize(int playerCount) => SupportedSizes.Contains(playerCount);

    /// <summary>
    /// Returns matchups (no court numbers) for round <paramref name="roundIndex"/> (0-based,
    /// max <paramref name="players"/>.Count - 2) of the Whist Cyclic schedule.
    /// </summary>
    public static List<Match> GetRoundMatchups(List<Player> players, int roundIndex)
    {
        if (!IsSupportedSize(players.Count))
            throw new ArgumentException($"Unsupported player count: {players.Count}", nameof(players));
        if (roundIndex < 0 || roundIndex >= players.Count - 1)
            throw new ArgumentOutOfRangeException(nameof(roundIndex));

        var baseRound = BaseRounds[players.Count];
        var n = players.Count;
        var rotateMod = n - 1;

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

    private static int ResolveRole(string role, int roundIndex, int rotateMod, List<Player> players)
    {
        if (role == "inf") return players[0].Id;
        var i = int.Parse(role);
        var rotated = (i + roundIndex) % rotateMod;
        return players[1 + rotated].Id;
    }

    private record BaseMatch(string A, string B, string C, string D);

    private static readonly IReadOnlyDictionary<int, BaseMatch[]> BaseRounds =
        new Dictionary<int, BaseMatch[]>
        {
            // Wh(8): players ∞, 0..6. Base round below; rotation mod 7.
            // Verified by WhistCyclicScheduleTests.AllPairsPartnerOnceAndOpposeTwice.
            [8] = new[]
            {
                new BaseMatch("inf", "0", "1", "3"),
                new BaseMatch("2",   "6", "4", "5"),
            },
        };
}
```

- [ ] **Step 2: Add the invariant test for Wh(8)**

Append to `PickleballScheduler.Tests/Services/WhistCyclicScheduleTests.cs`:

```csharp
[Theory]
[InlineData(8)]
public void AllPairsPartnerOnceAndOpposeTwice(int playerCount)
{
    var players = Enumerable.Range(1, playerCount)
        .Select(id => new Player { Id = id, Name = $"P{id}" })
        .ToList();

    var partnerCounts = new Dictionary<string, int>();
    var opponentCounts = new Dictionary<string, int>();

    for (int r = 0; r < playerCount - 1; r++)
    {
        var matches = WhistCyclicSchedule.GetRoundMatchups(players, r);
        Assert.Equal(playerCount / 4, matches.Count);

        var seen = new HashSet<int>();
        foreach (var m in matches)
        {
            foreach (var pid in new[] { m.Team1Player1Id, m.Team1Player2Id, m.Team2Player1Id, m.Team2Player2Id })
            {
                Assert.True(seen.Add(pid),
                    $"Player {pid} double-booked in round {r}");
            }

            Increment(partnerCounts, m.Team1Player1Id, m.Team1Player2Id);
            Increment(partnerCounts, m.Team2Player1Id, m.Team2Player2Id);
            foreach (var p1 in new[] { m.Team1Player1Id, m.Team1Player2Id })
                foreach (var p2 in new[] { m.Team2Player1Id, m.Team2Player2Id })
                    Increment(opponentCounts, p1, p2);
        }

        Assert.Equal(playerCount, seen.Count);
    }

    foreach (var kv in partnerCounts)
        Assert.True(kv.Value == 1, $"Pair {kv.Key} partnered {kv.Value} times, expected 1");

    foreach (var kv in opponentCounts)
        Assert.True(kv.Value == 2, $"Pair {kv.Key} opposed {kv.Value} times, expected 2");

    var allPairs = new HashSet<string>();
    for (int a = 1; a <= playerCount; a++)
        for (int b = a + 1; b <= playerCount; b++)
            allPairs.Add($"{a}-{b}");
    Assert.True(allPairs.SetEquals(partnerCounts.Keys),
        $"Missing partner pairs: {string.Join(", ", allPairs.Except(partnerCounts.Keys))}");
    Assert.True(allPairs.SetEquals(opponentCounts.Keys),
        $"Missing opponent pairs: {string.Join(", ", allPairs.Except(opponentCounts.Keys))}");
}

private static void Increment(Dictionary<string, int> counts, int a, int b)
{
    var key = a < b ? $"{a}-{b}" : $"{b}-{a}";
    counts[key] = counts.GetValueOrDefault(key) + 1;
}
```

- [ ] **Step 3: Run — expect PASS for Wh(8)**

```
dotnet test PickleballScheduler.Tests/PickleballScheduler.Tests.csproj --filter "FullyQualifiedName~WhistCyclicScheduleTests"
```

Expected: 11/11 pass (10 from `IsSupportedSize` + 1 invariant test for size 8).

- [ ] **Step 4: If the invariant test fails for Wh(8), try the alternate base round**

If Step 3 reports counts other than 1/2 for some pair, replace the Wh(8) base round with this alternate (also from a published Whist construction):

```csharp
[8] = new[]
{
    new BaseMatch("inf", "0", "3", "4"),
    new BaseMatch("1",   "6", "2", "5"),
},
```

Re-run Step 3. If still failing, search the literature ("Whist Cyclic base round Wh(8) Anderson Stinson") for a different transcription and try again. The test pinpoints which pair is wrong.

- [ ] **Step 5: Commit**

```
git add PickleballScheduler/Services/WhistCyclicSchedule.cs \
  PickleballScheduler.Tests/Services/WhistCyclicScheduleTests.cs
git commit -m "feat: implement Wh(8) base round and Whist invariant test"
```

---

## Task 3: Add Wh(12), Wh(16), Wh(20), Wh(24) base rounds

**Files:**
- Modify: `PickleballScheduler/Services/WhistCyclicSchedule.cs` (extend the `BaseRounds` dictionary)
- Modify: `PickleballScheduler.Tests/Services/WhistCyclicScheduleTests.cs` (extend the `[InlineData]` rows)

The rotation logic from Task 2 is already general; we just need to add the four remaining base rounds and run the invariant test against them.

### Notes for transcription

The base rounds below are candidates from common published Whist constructions. If any fails the invariant test, the test report says exactly which pair is over/under-counted. Try a different transcription from another reliable source (Anderson 1997, Stinson 2004, or the online Whist tournament catalogs at e.g. `https://en.wikipedia.org/wiki/Whist_tournament`).

Wh(12) candidate base round:
- Match 1: `(∞, 0)` vs `(1, 2)`
- Match 2: `(3, 7)` vs `(4, 8)`
- Match 3: `(5, 9)` vs `(6, 10)`

Wh(16) candidate base round:
- Match 1: `(∞, 0)` vs `(1, 2)`
- Match 2: `(3, 7)` vs `(4, 11)`
- Match 3: `(5, 13)` vs `(6, 9)`
- Match 4: `(8, 14)` vs `(10, 12)`

Wh(20) candidate base round:
- Match 1: `(∞, 0)` vs `(1, 2)`
- Match 2: `(3, 8)` vs `(5, 12)`
- Match 3: `(4, 13)` vs `(7, 17)`
- Match 4: `(6, 14)` vs `(10, 16)`
- Match 5: `(9, 15)` vs `(11, 18)`

Wh(24) candidate base round:
- Match 1: `(∞, 0)` vs `(1, 2)`
- Match 2: `(3, 8)` vs `(5, 14)`
- Match 3: `(4, 11)` vs `(7, 17)`
- Match 4: `(6, 18)` vs `(10, 21)`
- Match 5: `(9, 19)` vs `(12, 16)`
- Match 6: `(13, 20)` vs `(15, 22)`

**These are placeholders.** The published correct base rounds for Wh(12), Wh(16), Wh(20), Wh(24) need transcription from a definitive source. Apply Step 4's procedure: try, run the invariant test, fix until green.

- [ ] **Step 1: Add the four base rounds**

In `PickleballScheduler/Services/WhistCyclicSchedule.cs`, extend `BaseRounds`:

```csharp
private static readonly IReadOnlyDictionary<int, BaseMatch[]> BaseRounds =
    new Dictionary<int, BaseMatch[]>
    {
        [8] = new[]
        {
            new BaseMatch("inf", "0", "1", "3"),
            new BaseMatch("2",   "6", "4", "5"),
        },
        [12] = new[]
        {
            new BaseMatch("inf", "0", "1", "2"),
            new BaseMatch("3",   "7", "4", "8"),
            new BaseMatch("5",   "9", "6", "10"),
        },
        [16] = new[]
        {
            new BaseMatch("inf", "0",  "1", "2"),
            new BaseMatch("3",   "7",  "4", "11"),
            new BaseMatch("5",   "13", "6", "9"),
            new BaseMatch("8",   "14", "10","12"),
        },
        [20] = new[]
        {
            new BaseMatch("inf", "0",  "1", "2"),
            new BaseMatch("3",   "8",  "5", "12"),
            new BaseMatch("4",   "13", "7", "17"),
            new BaseMatch("6",   "14", "10","16"),
            new BaseMatch("9",   "15", "11","18"),
        },
        [24] = new[]
        {
            new BaseMatch("inf", "0",  "1",  "2"),
            new BaseMatch("3",   "8",  "5",  "14"),
            new BaseMatch("4",   "11", "7",  "17"),
            new BaseMatch("6",   "18", "10", "21"),
            new BaseMatch("9",   "19", "12", "16"),
            new BaseMatch("13",  "20", "15", "22"),
        },
    };
```

- [ ] **Step 2: Extend the invariant test theory data**

In `PickleballScheduler.Tests/Services/WhistCyclicScheduleTests.cs`, change:

```csharp
[Theory]
[InlineData(8)]
public void AllPairsPartnerOnceAndOpposeTwice(int playerCount)
```

to:

```csharp
[Theory]
[InlineData(8)]
[InlineData(12)]
[InlineData(16)]
[InlineData(20)]
[InlineData(24)]
public void AllPairsPartnerOnceAndOpposeTwice(int playerCount)
```

- [ ] **Step 3: Run — fix any failing transcriptions**

```
dotnet test PickleballScheduler.Tests/PickleballScheduler.Tests.csproj --filter "FullyQualifiedName~WhistCyclicScheduleTests"
```

If any size fails, the error message identifies the problematic pair. Look up that base round in a different source and replace the candidate. Repeat until all 5 sizes pass.

When green: 15/15 (10 from `IsSupportedSize` + 5 invariant cases).

- [ ] **Step 4: Confirm full suite**

```
dotnet test PickleballScheduler.Tests/PickleballScheduler.Tests.csproj --filter "FullyQualifiedName!~ScheduleDistributionTests" --nologo --verbosity quiet 2>&1 | tail -5
```

Expected: 50 passing (35 prior + 15 from `WhistCyclicScheduleTests`), 0 failing.

- [ ] **Step 5: Commit**

```
git add PickleballScheduler/Services/WhistCyclicSchedule.cs \
  PickleballScheduler.Tests/Services/WhistCyclicScheduleTests.cs
git commit -m "feat: add Wh(12), Wh(16), Wh(20), Wh(24) base rounds"
```

---

## Task 4: Wire Whist into `ScheduleGenerator.Generate`

**Files:**
- Modify: `PickleballScheduler/Services/ScheduleGenerator.cs`
- Modify: `PickleballScheduler.Tests/Services/ScheduleGeneratorTests.cs`

Adds the Whist branch in `Generate`'s per-round loop. The downstream HR1/HR2 counters and tracking-update block stay unchanged.

- [ ] **Step 1: Add an integration test for Paul's case asserting Whist properties**

Append to `PickleballScheduler.Tests/Services/ScheduleGeneratorTests.cs`:

```csharp
[Fact]
public void Generate_8Players_2Courts_7Rounds_PerfectWhistCycle()
{
    var players = MakePlayers(8);
    var generator = new ScheduleGenerator();

    var result = generator.Generate(players, numberOfCourts: 2, numberOfRounds: 7);

    Assert.Equal(0, result.Hr1Violations);
    Assert.Equal(0, result.Hr2Violations);

    var partnerCounts = new Dictionary<string, int>();
    var opponentCounts = new Dictionary<string, int>();
    foreach (var round in result.Rounds)
        foreach (var m in round.Matches)
        {
            IncrementPair(partnerCounts, m.Team1Player1Id, m.Team1Player2Id);
            IncrementPair(partnerCounts, m.Team2Player1Id, m.Team2Player2Id);
            foreach (var p1 in new[] { m.Team1Player1Id, m.Team1Player2Id })
                foreach (var p2 in new[] { m.Team2Player1Id, m.Team2Player2Id })
                    IncrementPair(opponentCounts, p1, p2);
        }

    Assert.Equal(28, partnerCounts.Count);
    Assert.All(partnerCounts.Values, v => Assert.Equal(1, v));
    Assert.Equal(28, opponentCounts.Count);
    Assert.All(opponentCounts.Values, v => Assert.Equal(2, v));
}

[Fact]
public void Generate_10Players_2Courts_9Rounds_FallsBackToJointSearch()
{
    // 10 is not a Whist size; the joint search should handle it without exceptions.
    var players = MakePlayers(10);
    var generator = new ScheduleGenerator();

    var result = generator.Generate(players, numberOfCourts: 2, numberOfRounds: 9);

    Assert.Equal(9, result.Rounds.Count);
    foreach (var round in result.Rounds)
    {
        var seen = new HashSet<int>();
        foreach (var m in round.Matches)
            foreach (var pid in new[] { m.Team1Player1Id, m.Team1Player2Id, m.Team2Player1Id, m.Team2Player2Id })
                Assert.True(seen.Add(pid), $"Player {pid} double-booked in round {round.RoundNumber}");
    }
}

private static void IncrementPair(Dictionary<string, int> counts, int a, int b)
{
    var key = a < b ? $"{a}-{b}" : $"{b}-{a}";
    counts[key] = counts.GetValueOrDefault(key) + 1;
}
```

- [ ] **Step 2: Run — both tests should fail at this point**

```
dotnet test PickleballScheduler.Tests/PickleballScheduler.Tests.csproj --filter "FullyQualifiedName~Generate_8Players_2Courts_7Rounds_PerfectWhistCycle|FullyQualifiedName~Generate_10Players_2Courts_9Rounds_FallsBackToJointSearch"
```

Expected: the Whist test almost certainly fails (joint search doesn't currently produce a perfect Whist schedule for 8p/2c/7r). The fallback test may pass (joint search already runs for 10p/2c/9r) — that's fine.

- [ ] **Step 3: Add the Whist branch in `Generate`**

In `PickleballScheduler/Services/ScheduleGenerator.cs`, find this block inside the round loop:

```csharp
var roundResult = BuildRound(activePlayers, players, partnerCounts, opponentCounts,
    lastOpponentRound, courtCounts, r, matchesPerRound);
var matches = roundResult.Matches;
```

Replace with:

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

- [ ] **Step 4: Run — expect both tests to pass**

```
dotnet test PickleballScheduler.Tests/PickleballScheduler.Tests.csproj --filter "FullyQualifiedName~Generate_8Players_2Courts_7Rounds_PerfectWhistCycle|FullyQualifiedName~Generate_10Players_2Courts_9Rounds_FallsBackToJointSearch"
```

Expected: 2/2 pass.

- [ ] **Step 5: Run all non-distribution tests**

```
dotnet test PickleballScheduler.Tests/PickleballScheduler.Tests.csproj --filter "FullyQualifiedName!~ScheduleDistributionTests" --nologo --verbosity quiet 2>&1 | tail -5
```

Expected: 52 passing (50 prior + 2 new), 0 failing. The pre-existing `Generate_8Players_2Courts_10Rounds_NoConsecutiveOpponents` test must also still pass — Whist rounds satisfy HR1=0/HR2=0, and joint search handles rounds 8–10 from there.

- [ ] **Step 6: Commit**

```
git add PickleballScheduler/Services/ScheduleGenerator.cs \
  PickleballScheduler.Tests/Services/ScheduleGeneratorTests.cs
git commit -m "feat: route supported sizes through Whist Cyclic schedule"
```

---

## Self-Review Notes

- **Spec coverage:**
  - Goal 1 (Whist for first n-1 rounds when qualifying) → Task 4 (the `useWhist` predicate matches the spec's three conditions exactly).
  - Goal 2 (fall back to joint search for non-qualifying configs) → Task 4 (the `else` branch).
  - Goal 3 (verify base rounds against invariants) → Tasks 2 & 3 (the parameterized `AllPairsPartnerOnceAndOpposeTwice` test).
  - Architecture (`internal static class`, two public methods) → Task 1 (`IsSupportedSize`, `GetRoundMatchups`).
  - Player labeling (∞ = `players[0]`, others = `players[1..]`) → Task 2 (`ResolveRole`).
  - Rotation (mod `n-1`) → Task 2 (`(i + roundIndex) % rotateMod`).
  - Base round source (Anderson 1997 / Stinson) → Tasks 2 & 3 (cited in code comments and plan notes).
  - Court assignment via existing `AssignCourts` → Task 4.
  - HR1/HR2 counters and persistence unchanged → confirmed in Task 4 (only the matchup branch is modified).
  - Banner behavior → unchanged because Whist gives HR1=0 / HR2=0, no spec section needs a code change.
  - Fallback test → Task 4.

- **Type consistency:**
  - `WhistCyclicSchedule.IsSupportedSize(int)` returns `bool` — used in `Generate`'s predicate.
  - `WhistCyclicSchedule.GetRoundMatchups(List<Player>, int)` returns `List<Match>` — assigned to `matches` in `Generate`.
  - `BaseMatch` is private, only used in `WhistCyclicSchedule`.
  - `AssignCourts(List<Match>, Dictionary<int, int[]>, int)` signature unchanged from existing code.

- **Open expectation flagged for the implementer:** the Wh(12)/Wh(16)/Wh(20)/Wh(24) base rounds in Task 3 are **candidates that may not all pass the invariant test**. This is explicit in the task notes; the recovery procedure (look up published source, replace, re-run test) is documented. Task 3's "fix any failing transcriptions" step expects iteration.
