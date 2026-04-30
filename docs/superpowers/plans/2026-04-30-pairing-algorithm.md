# Pairing Algorithm Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the two-pass per-round scheduler with a single joint search that enforces hard rules (no early partner repeats, no consecutive opponents) and balances partner/opponent spread, surface infeasibility via a banner, and add a distribution test that exercises many configurations.

**Architecture:** Per-round backtracking enumerates full match configurations and scores them with a five-element lex tuple `(hr1, hr2, Σ partnerCount², Σ opponentCount², courtImbalance)`. Hard-rule violation counts flow through a new `ScheduleResult` return type and persist on the `Event` entity so `Schedule.razor` can render a warning banner.

**Tech Stack:** .NET 8, Blazor Server, EF Core (SQLite), xUnit.

**Spec:** `docs/superpowers/specs/2026-04-30-pairing-algorithm-design.md`

---

## File Structure

**Create:**
- `PickleballScheduler/Services/ScheduleResult.cs` — record returned by `Generate`.
- `PickleballScheduler/Services/CostTuple.cs` — value type with comparator for the lex tuple.
- `PickleballScheduler.Tests/Services/ScheduleDistributionTests.cs` — theory + stress test.
- `PickleballScheduler.Tests/Services/CostTupleTests.cs` — comparator unit tests.
- `PickleballScheduler.Tests/Services/HardRuleHelperTests.cs` — pure-function HR1/HR2 detector tests.
- `PickleballScheduler/Migrations/<timestamp>_AddScheduleViolations.cs` — auto-generated migration.

**Modify:**
- `PickleballScheduler/Services/ScheduleGenerator.cs` — replace two-pass team/match selection with joint search; emit `ScheduleResult`.
- `PickleballScheduler/Models/Event.cs` — add `Hr1Violations`, `Hr2Violations`, `RepeatSuggestion`.
- `PickleballScheduler/Services/EventService.cs` — extend `SaveScheduleAsync` to persist violation data.
- `PickleballScheduler/Components/Pages/EventSetup.razor` — consume `ScheduleResult`, compute suggestion, persist counts.
- `PickleballScheduler/Components/Pages/Schedule.razor` — render banner when `evt.Hr1Violations > 0 || evt.Hr2Violations > 0`.
- `PickleballScheduler.Tests/Services/ScheduleGeneratorTests.cs` — update existing tests to read `result.Rounds`.

---

## Task 1: Add `ScheduleResult` record and switch `Generate` signature

**Files:**
- Create: `PickleballScheduler/Services/ScheduleResult.cs`
- Modify: `PickleballScheduler/Services/ScheduleGenerator.cs:7` (signature only)
- Modify: `PickleballScheduler/Components/Pages/EventSetup.razor:320`
- Modify: `PickleballScheduler.Tests/Services/ScheduleGeneratorTests.cs` (every call site)

This task changes the *return type* only — no behavior change. Hr1/Hr2 are reported as `0` until Task 2 wires up counters. RepeatSuggestion is `null` until Task 13.

- [ ] **Step 1: Create the record**

Create `PickleballScheduler/Services/ScheduleResult.cs`:

```csharp
using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public record ScheduleResult(
    List<Round> Rounds,
    int Hr1Violations,
    int Hr2Violations,
    string? RepeatSuggestion);
```

- [ ] **Step 2: Update `Generate` return type**

In `PickleballScheduler/Services/ScheduleGenerator.cs`, change the method signature on line 7 from:

```csharp
public List<Round> Generate(List<Player> players, int numberOfCourts, int numberOfRounds)
```

to:

```csharp
public ScheduleResult Generate(List<Player> players, int numberOfCourts, int numberOfRounds)
```

Replace the final line `return rounds;` with:

```csharp
return new ScheduleResult(rounds, Hr1Violations: 0, Hr2Violations: 0, RepeatSuggestion: null);
```

- [ ] **Step 3: Update test call sites**

Every existing test in `PickleballScheduler.Tests/Services/ScheduleGeneratorTests.cs` calls `generator.Generate(...)` and stores the result in `var rounds`. Replace each with two lines:

```csharp
var result = generator.Generate(players, numberOfCourts: X, numberOfRounds: Y);
var rounds = result.Rounds;
```

There are 7 such call sites (search for `generator.Generate(` in the file).

- [ ] **Step 4: Update `EventSetup.razor` call site**

In `PickleballScheduler/Components/Pages/EventSetup.razor` line 320, change:

```csharp
var rounds = ScheduleGenerator.Generate(dbPlayers, numberOfCourts, numberOfRounds);
await EventService.SaveScheduleAsync(evt.Id, rounds);
```

to:

```csharp
var result = ScheduleGenerator.Generate(dbPlayers, numberOfCourts, numberOfRounds);
await EventService.SaveScheduleAsync(evt.Id, result.Rounds);
```

(The expanded `SaveScheduleAsync` signature comes in Task 11.)

- [ ] **Step 5: Build and run tests**

```
dotnet build
dotnet test
```

Expected: all 8 existing tests pass.

- [ ] **Step 6: Commit**

```
git add PickleballScheduler/Services/ScheduleResult.cs \
  PickleballScheduler/Services/ScheduleGenerator.cs \
  PickleballScheduler/Components/Pages/EventSetup.razor \
  PickleballScheduler.Tests/Services/ScheduleGeneratorTests.cs
git commit -m "refactor: return ScheduleResult from Generate (no behavior change)"
```

---

## Task 2: Add HR1/HR2 violation counting (observability before action)

**Files:**
- Modify: `PickleballScheduler/Services/ScheduleGenerator.cs`
- Modify: `PickleballScheduler.Tests/Services/ScheduleGeneratorTests.cs`

Counts hard-rule violations as the *existing* algorithm runs. The current algorithm doesn't enforce HR2, so this will reveal real violations on configs the algorithm currently fails on. The algorithm itself is unchanged — Task 7 will rewrite it.

- [ ] **Step 1: Write the failing test**

Add to `PickleballScheduler.Tests/Services/ScheduleGeneratorTests.cs`:

```csharp
[Fact]
public void Generate_8Players_2Courts_5Rounds_ReportsHr2Violations()
{
    // The current algorithm doesn't avoid consecutive opponents.
    // Once Task 7 lands, this test gets updated to assert == 0.
    var players = MakePlayers(8);
    var generator = new ScheduleGenerator();

    var result = generator.Generate(players, numberOfCourts: 2, numberOfRounds: 5);

    // Sanity: counts are integers >= 0
    Assert.True(result.Hr1Violations >= 0);
    Assert.True(result.Hr2Violations >= 0);
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test --filter "FullyQualifiedName~Generate_8Players_2Courts_5Rounds_ReportsHr2Violations"
```

Expected: FAIL — counts are not yet computed (compile error or asserts fire if Task 1 hardcoded them at 0; the test currently passes vacuously, so revisit after Step 5).

Actually this assertion will pass even with hardcoded 0. Update the test to:

```csharp
[Fact]
public void Generate_4Players_1Court_3Rounds_AnyHr2Reported()
{
    // 4 players / 1 court forces HR2 violations every round (you face the only 2 opponents).
    // Current algorithm doesn't avoid them. After Task 2, count should be > 0.
    var players = MakePlayers(4);
    var generator = new ScheduleGenerator();

    var result = generator.Generate(players, numberOfCourts: 1, numberOfRounds: 3);

    Assert.True(result.Hr2Violations > 0,
        $"Expected HR2 violations on 4p/1c/3r config, got {result.Hr2Violations}");
}
```

Run it. Expected: FAIL because `Hr2Violations` is hardcoded to 0.

- [ ] **Step 3: Implement HR1/HR2 counting in `ScheduleGenerator.Generate`**

Add two local accumulators before the main `for` loop:

```csharp
int hr1Violations = 0;
int hr2Violations = 0;
var lastOpponentRound = new Dictionary<string, int>();
```

Inside the per-round tracking loop (after computing `matches`, before the existing tracking block at line 54), insert:

```csharp
// HR1: forced repeats — pair partner count was strictly greater than the
// minimum partner count among pairs sharing a player with this pair, BEFORE this round.
foreach (var match in matches)
{
    foreach (var pair in new[] {
        (match.Team1Player1Id, match.Team1Player2Id),
        (match.Team2Player1Id, match.Team2Player2Id) })
    {
        var key = PairKey(pair.Item1, pair.Item2);
        var priorCount = partnerCounts.GetValueOrDefault(key);
        if (priorCount == 0) continue;
        var minSiblingCount = MinSiblingPartnerCount(pair.Item1, pair.Item2, players, partnerCounts);
        if (priorCount > minSiblingCount) hr1Violations++;
    }
}

// HR2: opponent pair faced last round.
foreach (var match in matches)
{
    var team1 = new[] { match.Team1Player1Id, match.Team1Player2Id };
    var team2 = new[] { match.Team2Player1Id, match.Team2Player2Id };
    foreach (var p1 in team1)
        foreach (var p2 in team2)
        {
            var key = PairKey(p1, p2);
            if (lastOpponentRound.GetValueOrDefault(key, -10) == r - 1)
                hr2Violations++;
        }
}
```

After the existing opponent-counts block, also update `lastOpponentRound` for each opposing pair just recorded:

```csharp
foreach (var p1 in team1)
    foreach (var p2 in team2)
    {
        var ok = PairKey(p1, p2);
        opponentCounts[ok] = opponentCounts.GetValueOrDefault(ok) + 1;
        lastOpponentRound[ok] = r;  // NEW
    }
```

Add the helper method at the bottom of the class:

```csharp
private static int MinSiblingPartnerCount(
    int a, int b, List<Player> players, Dictionary<string, int> partnerCounts)
{
    int min = int.MaxValue;
    foreach (var p in players)
    {
        if (p.Id == a || p.Id == b) continue;
        var ka = PairKey(a, p.Id);
        var kb = PairKey(b, p.Id);
        var ca = partnerCounts.GetValueOrDefault(ka);
        var cb = partnerCounts.GetValueOrDefault(kb);
        if (ca < min) min = ca;
        if (cb < min) min = cb;
    }
    return min == int.MaxValue ? 0 : min;
}
```

Update the return:

```csharp
return new ScheduleResult(rounds, hr1Violations, hr2Violations, RepeatSuggestion: null);
```

- [ ] **Step 4: Run the failing test — expect it to pass**

```
dotnet test --filter "FullyQualifiedName~Generate_4Players_1Court_3Rounds_AnyHr2Reported"
```

Expected: PASS (HR2 count is positive).

- [ ] **Step 5: Run full test suite**

```
dotnet test
```

Expected: all existing tests still pass.

- [ ] **Step 6: Commit**

```
git add PickleballScheduler/Services/ScheduleGenerator.cs \
  PickleballScheduler.Tests/Services/ScheduleGeneratorTests.cs
git commit -m "feat: count HR1/HR2 violations during schedule generation"
```

---

## Task 3: Add distribution feasibility helpers

**Files:**
- Create: `PickleballScheduler.Tests/Services/ScheduleFeasibility.cs` (test-project static helpers)
- Create: `PickleballScheduler.Tests/Services/ScheduleFeasibilityTests.cs`

These helpers tell the distribution test what thresholds to expect for a given configuration.

- [ ] **Step 1: Write tests for the helpers**

Create `PickleballScheduler.Tests/Services/ScheduleFeasibilityTests.cs`:

```csharp
namespace PickleballScheduler.Tests.Services;

public class ScheduleFeasibilityTests
{
    [Theory]
    [InlineData(8, 1, 7, 0)]   // 8 players, 1 court → 4 active per round, but 8 distinct partners possible across rotation
    [InlineData(8, 2, 7, 0)]   // 8 players, 2 courts, 7 rounds → fits a perfect 1-factorization
    [InlineData(8, 2, 14, 0)]  // 14 = 2 * 7, double round-robin still fits
    [InlineData(8, 2, 15, 1)]  // 15 partners across 7 possible → at least one pair must repeat 3 times (max=3, min=2)
    public void ExpectedMinForcedRepeats_KnownCases(int players, int courts, int rounds, int expected)
    {
        Assert.Equal(expected, ScheduleFeasibility.ExpectedMinForcedRepeats(players, courts, rounds));
    }

    [Theory]
    [InlineData(4, 1, false)]   // only 2 opponents possible — every round repeats
    [InlineData(6, 1, true)]    // bye rotation gives room
    [InlineData(8, 2, true)]
    public void Hr2Feasible_Cases(int players, int courts, bool expected)
    {
        Assert.Equal(expected, ScheduleFeasibility.Hr2Feasible(players, courts));
    }
}
```

- [ ] **Step 2: Run — expect FAIL (compile error: helper class doesn't exist)**

```
dotnet test --filter "FullyQualifiedName~ScheduleFeasibilityTests"
```

- [ ] **Step 3: Implement the helpers**

Create `PickleballScheduler.Tests/Services/ScheduleFeasibility.cs`:

```csharp
namespace PickleballScheduler.Tests.Services;

internal static class ScheduleFeasibility
{
    /// <summary>
    /// Minimum number of forced partner-repeat events given total players, courts, and rounds.
    /// A "repeat event" = a pair that partners more times than the minimum count permitted by the constraint
    /// (each player should partner with each other player roughly the same number of times).
    /// </summary>
    public static int ExpectedMinForcedRepeats(int players, int courts, int rounds)
    {
        // Assume all players play every round (active = players when players % 4 == 0 and courts allow).
        // Each player has (players - 1) possible partners.
        // Each round each player partners with 1 person. Over `rounds` rounds, each player has `rounds` partner-encounters.
        // If rounds <= players - 1, the assignment is feasible without repeats — return 0.
        // Otherwise, at least one pair must repeat: each player has rounds partners over (players-1) slots.
        // Forced repeats per player = max(0, rounds - (players - 1)).
        // Total forced repeats (counting each pair once) = floor(players * forcedPerPlayer / 2).
        var forcedPerPlayer = Math.Max(0, rounds - (players - 1));
        return players * forcedPerPlayer / 2;
    }

    /// <summary>
    /// Whether HR2 (no consecutive opponents) is achievable for this configuration.
    /// False only when 4 players / 1 court — the same 4 players are always active and always face each other.
    /// </summary>
    public static bool Hr2Feasible(int players, int courts)
    {
        return !(players == 4 && courts == 1);
    }
}
```

- [ ] **Step 4: Run — expect PASS**

```
dotnet test --filter "FullyQualifiedName~ScheduleFeasibilityTests"
```

- [ ] **Step 5: Commit**

```
git add PickleballScheduler.Tests/Services/ScheduleFeasibility.cs \
  PickleballScheduler.Tests/Services/ScheduleFeasibilityTests.cs
git commit -m "test: add schedule feasibility helpers for distribution thresholds"
```

---

## Task 4: Add distribution theory test (will fail until Task 7 lands)

**Files:**
- Create: `PickleballScheduler.Tests/Services/ScheduleDistributionTests.cs`

Drives ~100 fixed `(playerCount, courtCount, roundCount, seed)` tuples generated once at compile time and asserts on fairness metrics.

- [ ] **Step 1: Create the test file with `TheoryData`**

```csharp
using PickleballScheduler.Models;
using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

public class ScheduleDistributionTests
{
    public static TheoryData<int, int, int, int> Configs()
    {
        var data = new TheoryData<int, int, int, int>();
        var rng = new Random(20260430);
        for (int i = 0; i < 100; i++)
        {
            int players = rng.Next(4, 25);   // 4..24
            int courts = rng.Next(1, 7);     // 1..6
            int rounds = rng.Next(3, 16);    // 3..15
            data.Add(players, courts, rounds, i);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Configs))]
    public void Generate_DistributionMetrics(int playerCount, int courtCount, int rounds, int seed)
    {
        var players = Enumerable.Range(1, playerCount)
            .Select(i => new Player { Id = i, Name = $"Player {i}" })
            .ToList();
        var generator = new ScheduleGenerator();

        var result = generator.Generate(players, courtCount, rounds);

        // HR1 violations
        var maxHr1 = ScheduleFeasibility.ExpectedMinForcedRepeats(playerCount, courtCount, rounds);
        Assert.True(result.Hr1Violations <= maxHr1,
            $"[{playerCount}p/{courtCount}c/{rounds}r seed={seed}] HR1={result.Hr1Violations} > max={maxHr1}");

        // HR2 violations
        if (ScheduleFeasibility.Hr2Feasible(playerCount, courtCount))
        {
            Assert.True(result.Hr2Violations == 0,
                $"[{playerCount}p/{courtCount}c/{rounds}r seed={seed}] HR2={result.Hr2Violations}, expected 0");
        }

        // Partner spread
        var partnerCounts = ComputePartnerCounts(result.Rounds);
        if (partnerCounts.Count > 0)
        {
            var spread = partnerCounts.Values.Max() - partnerCounts.Values.Min();
            Assert.True(spread <= 1,
                $"[{playerCount}p/{courtCount}c/{rounds}r seed={seed}] partner spread={spread}");
        }

        // Opponent spread
        var opponentCounts = ComputeOpponentCounts(result.Rounds);
        if (opponentCounts.Count > 0)
        {
            var spread = opponentCounts.Values.Max() - opponentCounts.Values.Min();
            Assert.True(spread <= 2,
                $"[{playerCount}p/{courtCount}c/{rounds}r seed={seed}] opponent spread={spread}");
        }

        // Bye fairness
        var byeCounts = ComputeByeCounts(players, result.Rounds);
        if (byeCounts.Values.Any())
        {
            var spread = byeCounts.Values.Max() - byeCounts.Values.Min();
            Assert.True(spread <= 1,
                $"[{playerCount}p/{courtCount}c/{rounds}r seed={seed}] bye spread={spread}");
        }

        // Per-player court spread
        foreach (var p in players)
        {
            var counts = ComputeCourtCountsForPlayer(p.Id, result.Rounds);
            if (counts.Count > 1)
            {
                var spread = counts.Values.Max() - counts.Values.Min();
                Assert.True(spread <= 2,
                    $"[{playerCount}p/{courtCount}c/{rounds}r seed={seed}] court spread for player {p.Id}={spread}");
            }
        }

        // Coverage: every pair partners when configuration permits.
        if (rounds >= playerCount - 1 && playerCount % 2 == 0 && playerCount / 2 <= courtCount)
        {
            var allPairs = new HashSet<string>();
            for (int a = 1; a <= playerCount; a++)
                for (int b = a + 1; b <= playerCount; b++)
                    allPairs.Add($"{a}-{b}");
            var seenPairs = partnerCounts.Keys.ToHashSet();
            Assert.True(allPairs.IsSubsetOf(seenPairs),
                $"[{playerCount}p/{courtCount}c/{rounds}r seed={seed}] missing pairs: {string.Join(", ", allPairs.Except(seenPairs))}");
        }
    }

    private static Dictionary<string, int> ComputePartnerCounts(List<Round> rounds)
    {
        var counts = new Dictionary<string, int>();
        foreach (var round in rounds)
            foreach (var match in round.Matches)
            {
                Increment(counts, match.Team1Player1Id, match.Team1Player2Id);
                Increment(counts, match.Team2Player1Id, match.Team2Player2Id);
            }
        return counts;
    }

    private static Dictionary<string, int> ComputeOpponentCounts(List<Round> rounds)
    {
        var counts = new Dictionary<string, int>();
        foreach (var round in rounds)
            foreach (var match in round.Matches)
            {
                var team1 = new[] { match.Team1Player1Id, match.Team1Player2Id };
                var team2 = new[] { match.Team2Player1Id, match.Team2Player2Id };
                foreach (var p1 in team1)
                    foreach (var p2 in team2)
                        Increment(counts, p1, p2);
            }
        return counts;
    }

    private static Dictionary<int, int> ComputeByeCounts(List<Player> players, List<Round> rounds)
    {
        var counts = players.ToDictionary(p => p.Id, _ => 0);
        foreach (var round in rounds)
            foreach (var bye in round.Byes)
                if (counts.ContainsKey(bye.PlayerId)) counts[bye.PlayerId]++;
        return counts;
    }

    private static Dictionary<int, int> ComputeCourtCountsForPlayer(int playerId, List<Round> rounds)
    {
        var counts = new Dictionary<int, int>();
        foreach (var round in rounds)
            foreach (var match in round.Matches)
            {
                var ids = new[] { match.Team1Player1Id, match.Team1Player2Id, match.Team2Player1Id, match.Team2Player2Id };
                if (ids.Contains(playerId))
                    counts[match.CourtNumber] = counts.GetValueOrDefault(match.CourtNumber) + 1;
            }
        return counts;
    }

    private static void Increment(Dictionary<string, int> counts, int a, int b)
    {
        var key = a < b ? $"{a}-{b}" : $"{b}-{a}";
        counts[key] = counts.GetValueOrDefault(key) + 1;
    }
}
```

- [ ] **Step 2: Run — expect MANY failures**

```
dotnet test --filter "FullyQualifiedName~ScheduleDistributionTests"
```

Expected: many failures. The current algorithm doesn't satisfy HR2 or the strict spread bounds. **This is the bar that Task 7 must clear.** Note the failures so we can confirm we cleared them.

- [ ] **Step 3: Commit (red bar — intentional)**

```
git add PickleballScheduler.Tests/Services/ScheduleDistributionTests.cs
git commit -m "test: add distribution theory test (red — fixed by joint search)"
```

---

## Task 5: Add `CostTuple` value type with comparator

**Files:**
- Create: `PickleballScheduler/Services/CostTuple.cs`
- Create: `PickleballScheduler.Tests/Services/CostTupleTests.cs`

Pure data + comparator. Used inside the joint search.

- [ ] **Step 1: Write tests**

Create `PickleballScheduler.Tests/Services/CostTupleTests.cs`:

```csharp
using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

public class CostTupleTests
{
    [Fact]
    public void Compare_Hr1Dominates()
    {
        var a = new CostTuple(Hr1: 1, Hr2: 0, PartnerSqSum: 0, OpponentSqSum: 0, CourtImbalance: 0);
        var b = new CostTuple(Hr1: 0, Hr2: 100, PartnerSqSum: 9999, OpponentSqSum: 9999, CourtImbalance: 9999);
        Assert.True(b.IsLessThan(a));
    }

    [Fact]
    public void Compare_Hr2DominatesPartner()
    {
        var a = new CostTuple(Hr1: 0, Hr2: 1, PartnerSqSum: 0, OpponentSqSum: 0, CourtImbalance: 0);
        var b = new CostTuple(Hr1: 0, Hr2: 0, PartnerSqSum: 9999, OpponentSqSum: 9999, CourtImbalance: 9999);
        Assert.True(b.IsLessThan(a));
    }

    [Fact]
    public void Compare_PartnerDominatesOpponent()
    {
        var a = new CostTuple(Hr1: 0, Hr2: 0, PartnerSqSum: 5, OpponentSqSum: 0, CourtImbalance: 0);
        var b = new CostTuple(Hr1: 0, Hr2: 0, PartnerSqSum: 4, OpponentSqSum: 9999, CourtImbalance: 9999);
        Assert.True(b.IsLessThan(a));
    }

    [Fact]
    public void Compare_OpponentDominatesCourt()
    {
        var a = new CostTuple(Hr1: 0, Hr2: 0, PartnerSqSum: 5, OpponentSqSum: 5, CourtImbalance: 0);
        var b = new CostTuple(Hr1: 0, Hr2: 0, PartnerSqSum: 5, OpponentSqSum: 4, CourtImbalance: 9999);
        Assert.True(b.IsLessThan(a));
    }

    [Fact]
    public void Compare_EqualTuplesAreEqual()
    {
        var a = new CostTuple(0, 0, 1, 2, 3);
        var b = new CostTuple(0, 0, 1, 2, 3);
        Assert.False(a.IsLessThan(b));
        Assert.False(b.IsLessThan(a));
    }
}
```

- [ ] **Step 2: Run — expect compile failure**

```
dotnet test --filter "FullyQualifiedName~CostTupleTests"
```

- [ ] **Step 3: Implement the type**

Create `PickleballScheduler/Services/CostTuple.cs`:

```csharp
namespace PickleballScheduler.Services;

public readonly record struct CostTuple(
    int Hr1,
    int Hr2,
    long PartnerSqSum,
    long OpponentSqSum,
    long CourtImbalance)
{
    public static CostTuple Worst => new(int.MaxValue, int.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue);

    public bool IsLessThan(CostTuple other)
    {
        if (Hr1 != other.Hr1) return Hr1 < other.Hr1;
        if (Hr2 != other.Hr2) return Hr2 < other.Hr2;
        if (PartnerSqSum != other.PartnerSqSum) return PartnerSqSum < other.PartnerSqSum;
        if (OpponentSqSum != other.OpponentSqSum) return OpponentSqSum < other.OpponentSqSum;
        return CourtImbalance < other.CourtImbalance;
    }

    public bool IsLessOrEqualTo(CostTuple other)
        => !other.IsLessThan(this);
}
```

- [ ] **Step 4: Run — expect PASS**

```
dotnet test --filter "FullyQualifiedName~CostTupleTests"
```

- [ ] **Step 5: Commit**

```
git add PickleballScheduler/Services/CostTuple.cs \
  PickleballScheduler.Tests/Services/CostTupleTests.cs
git commit -m "feat: add CostTuple with lexicographic comparator"
```

---

## Task 6: Add HR1/HR2 detection helpers as pure functions

**Files:**
- Modify: `PickleballScheduler/Services/ScheduleGenerator.cs` (add public static methods)
- Create: `PickleballScheduler.Tests/Services/HardRuleHelperTests.cs`

The detector helpers are pure: given current state, decide whether a candidate pair would violate.

- [ ] **Step 1: Write tests**

Create `PickleballScheduler.Tests/Services/HardRuleHelperTests.cs`:

```csharp
using PickleballScheduler.Models;
using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

public class HardRuleHelperTests
{
    private static List<Player> Players(int n) =>
        Enumerable.Range(1, n).Select(i => new Player { Id = i, Name = $"P{i}" }).ToList();

    [Fact]
    public void IsHr1Violation_FirstTimePartners_ReturnsFalse()
    {
        var partnerCounts = new Dictionary<string, int>();
        var players = Players(4);
        Assert.False(ScheduleGenerator.IsHr1Violation(1, 2, players, partnerCounts));
    }

    [Fact]
    public void IsHr1Violation_AllAtSameCount_ReturnsFalse()
    {
        // 4 players, every pair has partnered once. Repeating any pair is now allowed.
        var players = Players(4);
        var partnerCounts = new Dictionary<string, int>
        {
            ["1-2"] = 1, ["1-3"] = 1, ["1-4"] = 1,
            ["2-3"] = 1, ["2-4"] = 1, ["3-4"] = 1,
        };
        Assert.False(ScheduleGenerator.IsHr1Violation(1, 2, players, partnerCounts));
    }

    [Fact]
    public void IsHr1Violation_SiblingHasLowerCount_ReturnsTrue()
    {
        // Pair (1,2) has count 1; pair (1,3) has count 0. Repeating (1,2) is a violation.
        var players = Players(4);
        var partnerCounts = new Dictionary<string, int> { ["1-2"] = 1 };
        Assert.True(ScheduleGenerator.IsHr1Violation(1, 2, players, partnerCounts));
    }

    [Fact]
    public void IsHr2Violation_PreviousRoundOpponents_ReturnsTrue()
    {
        var lastOpp = new Dictionary<string, int> { ["1-3"] = 4 };
        Assert.True(ScheduleGenerator.IsHr2Violation(1, 3, lastOpp, currentRound: 5));
    }

    [Fact]
    public void IsHr2Violation_TwoRoundsAgo_ReturnsFalse()
    {
        var lastOpp = new Dictionary<string, int> { ["1-3"] = 3 };
        Assert.False(ScheduleGenerator.IsHr2Violation(1, 3, lastOpp, currentRound: 5));
    }

    [Fact]
    public void IsHr2Violation_NeverFaced_ReturnsFalse()
    {
        var lastOpp = new Dictionary<string, int>();
        Assert.False(ScheduleGenerator.IsHr2Violation(1, 3, lastOpp, currentRound: 5));
    }
}
```

- [ ] **Step 2: Run — expect compile failure**

```
dotnet test --filter "FullyQualifiedName~HardRuleHelperTests"
```

- [ ] **Step 3: Implement the helpers**

Add to `PickleballScheduler/Services/ScheduleGenerator.cs` as `public static` methods:

```csharp
public static bool IsHr1Violation(
    int a, int b, List<Player> players, Dictionary<string, int> partnerCounts)
{
    var key = PairKey(a, b);
    var thisCount = partnerCounts.GetValueOrDefault(key);
    if (thisCount == 0) return false;

    int minSibling = int.MaxValue;
    foreach (var p in players)
    {
        if (p.Id == a || p.Id == b) continue;
        var ka = PairKey(a, p.Id);
        var kb = PairKey(b, p.Id);
        var ca = partnerCounts.GetValueOrDefault(ka);
        var cb = partnerCounts.GetValueOrDefault(kb);
        if (ca < minSibling) minSibling = ca;
        if (cb < minSibling) minSibling = cb;
    }
    if (minSibling == int.MaxValue) minSibling = 0;
    return thisCount > minSibling;
}

public static bool IsHr2Violation(
    int a, int b, Dictionary<string, int> lastOpponentRound, int currentRound)
{
    var key = PairKey(a, b);
    return lastOpponentRound.GetValueOrDefault(key, -10) == currentRound - 1;
}
```

Also raise `PairKey` from `internal` to `public static` so tests can use it directly if needed (line 375):

```csharp
public static string PairKey(int a, int b)
    => a < b ? $"{a}-{b}" : $"{b}-{a}";
```

Replace the inline `MinSiblingPartnerCount` helper added in Task 2 with calls to `IsHr1Violation` (the inline version becomes dead code; remove it). The HR1 counter loop becomes:

```csharp
foreach (var match in matches)
{
    foreach (var pair in new[] {
        (match.Team1Player1Id, match.Team1Player2Id),
        (match.Team2Player1Id, match.Team2Player2Id) })
    {
        if (IsHr1Violation(pair.Item1, pair.Item2, players, partnerCounts))
            hr1Violations++;
    }
}
```

The HR2 counter loop becomes:

```csharp
foreach (var match in matches)
{
    var team1 = new[] { match.Team1Player1Id, match.Team1Player2Id };
    var team2 = new[] { match.Team2Player1Id, match.Team2Player2Id };
    foreach (var p1 in team1)
        foreach (var p2 in team2)
            if (IsHr2Violation(p1, p2, lastOpponentRound, r))
                hr2Violations++;
}
```

- [ ] **Step 4: Run — expect PASS**

```
dotnet test
```

All tests including the new helper tests pass; existing behavior unchanged.

- [ ] **Step 5: Commit**

```
git add PickleballScheduler/Services/ScheduleGenerator.cs \
  PickleballScheduler.Tests/Services/HardRuleHelperTests.cs
git commit -m "feat: add IsHr1Violation/IsHr2Violation pure helpers"
```

---

## Task 7: Replace partner+opponent passes with joint round search

**Files:**
- Modify: `PickleballScheduler/Services/ScheduleGenerator.cs`

The structural change. `FormTeams` and `PairTeamsIntoMatches` are deleted; a new `BuildRound(...)` method does both jobs in one backtracking search.

- [ ] **Step 1: Write a smoke test for joint search behavior**

Add to `PickleballScheduler.Tests/Services/ScheduleGeneratorTests.cs`:

```csharp
[Fact]
public void Generate_8Players_2Courts_10Rounds_NoConsecutiveOpponents()
{
    // Paul's representative case. After the joint search lands, HR2 must be 0.
    var players = MakePlayers(8);
    var generator = new ScheduleGenerator();

    var result = generator.Generate(players, numberOfCourts: 2, numberOfRounds: 10);

    Assert.Equal(0, result.Hr1Violations);
    Assert.Equal(0, result.Hr2Violations);
}
```

- [ ] **Step 2: Run — expect FAIL with current algorithm**

```
dotnet test --filter "FullyQualifiedName~Generate_8Players_2Courts_10Rounds_NoConsecutiveOpponents"
```

Expected: FAIL.

- [ ] **Step 3: Add the new `BuildRound` method**

Add to `ScheduleGenerator`:

```csharp
private record RoundCandidate(List<Match> Matches, CostTuple Cost);

private static RoundCandidate BuildRound(
    List<Player> activePlayers,
    List<Player> allPlayers,
    Dictionary<string, int> partnerCounts,
    Dictionary<string, int> opponentCounts,
    Dictionary<string, int> lastOpponentRound,
    Dictionary<int, int[]> courtCounts,
    int currentRound,
    int numberOfCourts)
{
    var best = new RoundCandidate(new List<Match>(), CostTuple.Worst);
    var used = new bool[activePlayers.Count];
    var current = new List<Match>(activePlayers.Count / 4);

    SearchMatches(activePlayers, allPlayers, partnerCounts, opponentCounts,
        lastOpponentRound, courtCounts, currentRound, numberOfCourts,
        used, current,
        runningHr1: 0, runningHr2: 0, runningPartnerSq: 0, runningOpponentSq: 0,
        ref best);

    // Court assignment is a separate pass — court contributes only level 5,
    // and the matchups themselves are independent of court number.
    AssignCourts(best.Matches, courtCounts, numberOfCourts);

    return best;
}

private static void SearchMatches(
    List<Player> active,
    List<Player> allPlayers,
    Dictionary<string, int> partnerCounts,
    Dictionary<string, int> opponentCounts,
    Dictionary<string, int> lastOpponentRound,
    Dictionary<int, int[]> courtCounts,
    int currentRound,
    int numberOfCourts,
    bool[] used,
    List<Match> current,
    int runningHr1,
    int runningHr2,
    long runningPartnerSq,
    long runningOpponentSq,
    ref RoundCandidate best)
{
    // Compute partial cost. PartnerSqSum and OpponentSqSum are partial sums of squared post-update counts;
    // since each new match strictly increases them, the partial tuple is a valid lower bound.
    var partial = new CostTuple(
        runningHr1, runningHr2, runningPartnerSq, runningOpponentSq, CourtImbalance: 0);
    if (best.Cost.IsLessOrEqualTo(partial)) return;

    // Find first unused active player.
    int first = -1;
    for (int i = 0; i < active.Count; i++)
        if (!used[i]) { first = i; break; }

    if (first == -1)
    {
        // Round complete.
        var finalCost = new CostTuple(
            runningHr1, runningHr2, runningPartnerSq, runningOpponentSq, CourtImbalance: 0);
        if (finalCost.IsLessThan(best.Cost))
        {
            best = new RoundCandidate(new List<Match>(current.Select(m => new Match
            {
                Team1Player1Id = m.Team1Player1Id,
                Team1Player2Id = m.Team1Player2Id,
                Team2Player1Id = m.Team2Player1Id,
                Team2Player2Id = m.Team2Player2Id,
                CourtNumber = m.CourtNumber,
            })), finalCost);
        }
        return;
    }

    // Pick a partner for `active[first]`.
    used[first] = true;
    var p1 = active[first];

    for (int j = first + 1; j < active.Count; j++)
    {
        if (used[j]) continue;
        var p1p2 = active[j];
        var partnerKey = PairKey(p1.Id, p1p2.Id);
        var partnerHr1 = IsHr1Violation(p1.Id, p1p2.Id, allPlayers, partnerCounts) ? 1 : 0;
        var partnerNewCount = partnerCounts.GetValueOrDefault(partnerKey) + 1;
        var partnerSqDelta = (long)partnerNewCount * partnerNewCount
                           - (long)(partnerNewCount - 1) * (partnerNewCount - 1);

        used[j] = true;

        // Pick the next anchor (lowest unused), then its partner — together with (first, j) forms one match.
        int anchor2 = -1;
        for (int k = first + 1; k < active.Count; k++)
            if (!used[k]) { anchor2 = k; break; }

        if (anchor2 == -1)
        {
            // Odd number — should not happen for active = 4k. Skip.
            used[j] = false;
            continue;
        }

        var p2 = active[anchor2];
        used[anchor2] = true;
        for (int m = anchor2 + 1; m < active.Count; m++)
        {
            if (used[m]) continue;
            var p2p2 = active[m];
            var partner2Key = PairKey(p2.Id, p2p2.Id);
            var partner2Hr1 = IsHr1Violation(p2.Id, p2p2.Id, allPlayers, partnerCounts) ? 1 : 0;
            var partner2NewCount = partnerCounts.GetValueOrDefault(partner2Key) + 1;
            var partner2SqDelta = (long)partner2NewCount * partner2NewCount
                                - (long)(partner2NewCount - 1) * (partner2NewCount - 1);

            // Compute opponent contributions for the 4 cross-pairs.
            int matchHr2 = 0;
            long matchOppSq = 0;
            int[] team1 = { p1.Id, p1p2.Id };
            int[] team2 = { p2.Id, p2p2.Id };
            foreach (var x in team1)
                foreach (var y in team2)
                {
                    var ok = PairKey(x, y);
                    if (IsHr2Violation(x, y, lastOpponentRound, currentRound)) matchHr2++;
                    var oNew = opponentCounts.GetValueOrDefault(ok) + 1;
                    matchOppSq += (long)oNew * oNew - (long)(oNew - 1) * (oNew - 1);
                }

            used[m] = true;
            current.Add(new Match
            {
                Team1Player1Id = p1.Id,
                Team1Player2Id = p1p2.Id,
                Team2Player1Id = p2.Id,
                Team2Player2Id = p2p2.Id,
            });

            // Mutate the running counts for recursion.
            partnerCounts[partnerKey] = partnerNewCount;
            partnerCounts[partner2Key] = partner2NewCount;
            foreach (var x in team1)
                foreach (var y in team2)
                {
                    var ok = PairKey(x, y);
                    opponentCounts[ok] = opponentCounts.GetValueOrDefault(ok) + 1;
                }

            SearchMatches(active, allPlayers, partnerCounts, opponentCounts,
                lastOpponentRound, courtCounts, currentRound, numberOfCourts,
                used, current,
                runningHr1 + partnerHr1 + partner2Hr1,
                runningHr2 + matchHr2,
                runningPartnerSq + partnerSqDelta + partner2SqDelta,
                runningOpponentSq + matchOppSq,
                ref best);

            // Undo.
            foreach (var x in team1)
                foreach (var y in team2)
                {
                    var ok = PairKey(x, y);
                    opponentCounts[ok]--;
                    if (opponentCounts[ok] == 0) opponentCounts.Remove(ok);
                }
            partnerCounts[partnerKey] = partnerNewCount - 1;
            if (partnerCounts[partnerKey] == 0) partnerCounts.Remove(partnerKey);
            partnerCounts[partner2Key] = partner2NewCount - 1;
            if (partnerCounts[partner2Key] == 0) partnerCounts.Remove(partner2Key);

            current.RemoveAt(current.Count - 1);
            used[m] = false;
        }
        used[anchor2] = false;
        used[j] = false;
    }
    used[first] = false;
}
```

- [ ] **Step 4: Replace per-round logic in `Generate`**

In the `Generate` method, replace the block that picks teams and builds matches (the `if (circleSchedule != null && r < circleRounds) { ... } else { ... }` block plus `PairTeamsIntoMatches` and `AssignCourts`) with:

```csharp
RoundCandidate roundResult;
if (circleSchedule != null && r < circleRounds)
{
    // Circle method fixes partners; still run the joint search over opponent splits for that fixed partner set.
    var fixedTeams = circleSchedule[r];
    roundResult = BuildRoundFromFixedTeams(fixedTeams, partnerCounts, opponentCounts,
        lastOpponentRound, courtCounts, r, matchesPerRound);
}
else
{
    roundResult = BuildRound(activePlayers, players, partnerCounts, opponentCounts,
        lastOpponentRound, courtCounts, r, matchesPerRound);
}

var matches = roundResult.Matches;
```

- [ ] **Step 5: Add the `BuildRoundFromFixedTeams` variant**

```csharp
private static RoundCandidate BuildRoundFromFixedTeams(
    List<(Player, Player)> fixedTeams,
    Dictionary<string, int> partnerCounts,
    Dictionary<string, int> opponentCounts,
    Dictionary<string, int> lastOpponentRound,
    Dictionary<int, int[]> courtCounts,
    int currentRound,
    int numberOfCourts)
{
    // Partners are fixed by the circle method; just enumerate ways to pair teams into matches.
    var best = new RoundCandidate(new List<Match>(), CostTuple.Worst);
    var used = new bool[fixedTeams.Count];
    var current = new List<Match>(fixedTeams.Count / 2);

    SearchTeamPairings(fixedTeams, opponentCounts, lastOpponentRound, currentRound,
        used, current, runningHr2: 0, runningOpponentSq: 0, ref best);

    // Partner cost is fixed (no choice), but we still need to roll partner sums into the cost
    // for accurate lex compare across rounds — for circle rounds it's irrelevant since we don't
    // compare circle vs non-circle within the same round.

    AssignCourts(best.Matches, courtCounts, numberOfCourts);
    return best;
}

private static void SearchTeamPairings(
    List<(Player, Player)> teams,
    Dictionary<string, int> opponentCounts,
    Dictionary<string, int> lastOpponentRound,
    int currentRound,
    bool[] used,
    List<Match> current,
    int runningHr2,
    long runningOpponentSq,
    ref RoundCandidate best)
{
    var partial = new CostTuple(0, runningHr2, 0, runningOpponentSq, 0);
    if (best.Cost.IsLessOrEqualTo(partial)) return;

    int first = -1;
    for (int i = 0; i < teams.Count; i++)
        if (!used[i]) { first = i; break; }

    if (first == -1)
    {
        var finalCost = new CostTuple(0, runningHr2, 0, runningOpponentSq, 0);
        if (finalCost.IsLessThan(best.Cost))
        {
            best = new RoundCandidate(new List<Match>(current.Select(m => new Match
            {
                Team1Player1Id = m.Team1Player1Id,
                Team1Player2Id = m.Team1Player2Id,
                Team2Player1Id = m.Team2Player1Id,
                Team2Player2Id = m.Team2Player2Id,
            })), finalCost);
        }
        return;
    }

    used[first] = true;
    var t1 = teams[first];
    for (int j = first + 1; j < teams.Count; j++)
    {
        if (used[j]) continue;
        var t2 = teams[j];
        int matchHr2 = 0;
        long matchOppSq = 0;
        int[] team1 = { t1.Item1.Id, t1.Item2.Id };
        int[] team2 = { t2.Item1.Id, t2.Item2.Id };
        foreach (var x in team1)
            foreach (var y in team2)
            {
                var ok = PairKey(x, y);
                if (IsHr2Violation(x, y, lastOpponentRound, currentRound)) matchHr2++;
                var oNew = opponentCounts.GetValueOrDefault(ok) + 1;
                matchOppSq += (long)oNew * oNew - (long)(oNew - 1) * (oNew - 1);
            }

        used[j] = true;
        current.Add(new Match
        {
            Team1Player1Id = t1.Item1.Id,
            Team1Player2Id = t1.Item2.Id,
            Team2Player1Id = t2.Item1.Id,
            Team2Player2Id = t2.Item2.Id,
        });
        foreach (var x in team1)
            foreach (var y in team2)
            {
                var ok = PairKey(x, y);
                opponentCounts[ok] = opponentCounts.GetValueOrDefault(ok) + 1;
            }

        SearchTeamPairings(teams, opponentCounts, lastOpponentRound, currentRound,
            used, current, runningHr2 + matchHr2, runningOpponentSq + matchOppSq, ref best);

        foreach (var x in team1)
            foreach (var y in team2)
            {
                var ok = PairKey(x, y);
                opponentCounts[ok]--;
                if (opponentCounts[ok] == 0) opponentCounts.Remove(ok);
            }
        current.RemoveAt(current.Count - 1);
        used[j] = false;
    }
    used[first] = false;
}
```

- [ ] **Step 6: Delete dead code**

In `ScheduleGenerator.cs`, remove the now-unused methods: `FormTeams`, `SearchTeams`, `PairCost`, `PairTeamsIntoMatches`, `FindBestTeamPairing`, `ScorePairing`, and `ShufflePlayers` (no longer needed since the joint search is deterministic and exhaustive).

- [ ] **Step 7: Run all tests**

```
dotnet test
```

Expected: all distribution-test cases pass, the new 8/2/10 smoke test passes, and the 4-player HR2 test from Task 2 still asserts `Hr2Violations > 0` (4p/1c is a known-infeasible case). If the 4p/1c test now reads 0 unexpectedly, investigate — but with `ScheduleFeasibility.Hr2Feasible(4, 1) == false`, the distribution test should accept any value there.

If the 16-active distribution cases time out, proceed to Task 15 (beam search) before continuing.

- [ ] **Step 8: Commit**

```
git add PickleballScheduler/Services/ScheduleGenerator.cs \
  PickleballScheduler.Tests/Services/ScheduleGeneratorTests.cs
git commit -m "feat: replace two-pass partner/opponent selection with joint round search"
```

---

## Task 8: Add distribution stress test

**Files:**
- Modify: `PickleballScheduler.Tests/Services/ScheduleDistributionTests.cs`

A `[Fact]` that runs 1000 random configs and asserts only structural invariants (no double-booking, every active player in exactly one match, court numbers in range, no exceptions).

- [ ] **Step 1: Add the stress test to `ScheduleDistributionTests`**

```csharp
[Fact]
public void StressTest_NoStructuralViolations_1000Configs()
{
    var rng = new Random(20260430);
    for (int i = 0; i < 1000; i++)
    {
        int playerCount = rng.Next(4, 25);
        int courts = rng.Next(1, 7);
        int rounds = rng.Next(3, 16);

        var players = Enumerable.Range(1, playerCount)
            .Select(id => new Player { Id = id, Name = $"P{id}" })
            .ToList();
        var generator = new ScheduleGenerator();

        ScheduleResult result;
        try
        {
            result = generator.Generate(players, courts, rounds);
        }
        catch (Exception ex)
        {
            Assert.Fail($"Threw on config p={playerCount} c={courts} r={rounds} (i={i}): {ex.Message}");
            return;
        }

        foreach (var round in result.Rounds)
        {
            var seen = new HashSet<int>();
            foreach (var match in round.Matches)
            {
                Assert.InRange(match.CourtNumber, 1, courts);
                foreach (var pid in new[] { match.Team1Player1Id, match.Team1Player2Id, match.Team2Player1Id, match.Team2Player2Id })
                {
                    Assert.True(seen.Add(pid),
                        $"Player {pid} double-booked in round {round.RoundNumber} (config p={playerCount} c={courts} r={rounds} i={i})");
                }
            }
        }
    }
}
```

- [ ] **Step 2: Run**

```
dotnet test --filter "FullyQualifiedName~StressTest_NoStructuralViolations"
```

Expected: PASS in a few seconds.

- [ ] **Step 3: Commit**

```
git add PickleballScheduler.Tests/Services/ScheduleDistributionTests.cs
git commit -m "test: add 1000-config stress test for structural invariants"
```

---

## Task 9: Add `Event` model fields and EF Core migration

**Files:**
- Modify: `PickleballScheduler/Models/Event.cs`
- Run: `dotnet ef migrations add AddScheduleViolations`
- Auto-create: `PickleballScheduler/Migrations/<timestamp>_AddScheduleViolations.cs`

- [ ] **Step 1: Add fields to `Event`**

In `PickleballScheduler/Models/Event.cs`, after the `Rounds` line:

```csharp
public int Hr1Violations { get; set; } = 0;
public int Hr2Violations { get; set; } = 0;
public string? RepeatSuggestion { get; set; }
```

- [ ] **Step 2: Generate the migration**

```
dotnet ef migrations add AddScheduleViolations -p PickleballScheduler -s PickleballScheduler
```

Expected: a new file in `PickleballScheduler/Migrations/` plus an updated `AppDbContextModelSnapshot.cs`.

- [ ] **Step 3: Apply the migration locally**

```
dotnet ef database update -p PickleballScheduler -s PickleballScheduler
```

- [ ] **Step 4: Build and run tests**

```
dotnet build
dotnet test
```

Expected: no test changes, all pass.

- [ ] **Step 5: Commit**

```
git add PickleballScheduler/Models/Event.cs \
  PickleballScheduler/Migrations/
git commit -m "feat: persist HR1/HR2 violation counts and repeat suggestion on Event"
```

---

## Task 10: Add `EventService.SaveScheduleAsync` overload taking `ScheduleResult`

**Files:**
- Modify: `PickleballScheduler/Services/EventService.cs`

Add a new overload alongside the existing `SaveScheduleAsync(int, List<Round>)`. The existing overload remains so this task compiles and commits independently. Task 12 switches the caller to the new overload and deletes the old one.

- [ ] **Step 1: Add the new overload**

In `PickleballScheduler/Services/EventService.cs`, after the existing `SaveScheduleAsync` method, add:

```csharp
public async Task SaveScheduleAsync(int eventId, ScheduleResult result)
{
    var evt = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId);
    if (evt == null) return;

    var existingRounds = await _db.Rounds
        .Where(r => r.EventId == eventId)
        .Include(r => r.Matches)
        .Include(r => r.Byes)
        .ToListAsync();

    foreach (var r in existingRounds)
    {
        _db.Matches.RemoveRange(r.Matches);
        _db.Byes.RemoveRange(r.Byes);
    }
    _db.Rounds.RemoveRange(existingRounds);

    foreach (var round in result.Rounds)
    {
        round.EventId = eventId;
        _db.Rounds.Add(round);
    }

    evt.Hr1Violations = result.Hr1Violations;
    evt.Hr2Violations = result.Hr2Violations;
    evt.RepeatSuggestion = result.RepeatSuggestion;

    await _db.SaveChangesAsync();
}
```

`ScheduleResult` is in the `PickleballScheduler.Services` namespace, the same as `EventService`, so no additional `using` is needed.

- [ ] **Step 2: Build and run tests**

```
dotnet build
dotnet test
```

Expected: build green, all tests pass (the old overload is still called from `EventSetup.razor`).

- [ ] **Step 3: Commit**

```
git add PickleballScheduler/Services/EventService.cs
git commit -m "feat: add EventService.SaveScheduleAsync overload that persists violation data"
```

---

## Task 11: Add repeat-suggestion calculator

**Files:**
- Modify: `PickleballScheduler/Services/ScheduleGenerator.cs` (add a static method)
- Modify: `PickleballScheduler.Tests/Services/ScheduleGeneratorTests.cs`

- [ ] **Step 1: Write a test**

```csharp
[Fact]
public void TrySuggestZeroViolationConfig_8Players_2Courts_3Rounds_ReturnsNull()
{
    // 8/2/3 already produces zero violations — no suggestion needed.
    var players = MakePlayers(8);
    var suggestion = ScheduleGenerator.TrySuggestZeroViolationConfig(
        players, courts: 2, rounds: 3);
    Assert.Null(suggestion);
}

[Fact]
public void TrySuggestZeroViolationConfig_4Players_1Court_5Rounds_SuggestsBetterConfig()
{
    // 4/1/5 forces HR2 every round. A near-neighbor config (e.g., 6/1/5) can clear it.
    var players = MakePlayers(4);
    var suggestion = ScheduleGenerator.TrySuggestZeroViolationConfig(
        players, courts: 1, rounds: 5);
    Assert.NotNull(suggestion);
    Assert.Contains("players", suggestion!);
}
```

- [ ] **Step 2: Run — expect compile failure**

- [ ] **Step 3: Implement**

Add to `ScheduleGenerator`:

```csharp
/// <summary>
/// Try near-neighbor configurations (player count and round count adjusted by ±1, ±2)
/// and return a human-readable suggestion for the smallest change that yields zero violations,
/// or null if no near-neighbor works.
/// </summary>
public static string? TrySuggestZeroViolationConfig(
    List<Player> players, int courts, int rounds)
{
    var generator = new ScheduleGenerator();

    // Search neighbors ordered by total |delta|, smallest first.
    var deltas = new List<(int dPlayers, int dRounds)>();
    for (int dp = -2; dp <= 2; dp++)
        for (int dr = -2; dr <= 2; dr++)
        {
            if (dp == 0 && dr == 0) continue;
            deltas.Add((dp, dr));
        }
    deltas.Sort((a, b) => (Math.Abs(a.dPlayers) + Math.Abs(a.dRounds))
                         .CompareTo(Math.Abs(b.dPlayers) + Math.Abs(b.dRounds)));

    foreach (var (dp, dr) in deltas)
    {
        int candidatePlayers = players.Count + dp;
        int candidateRounds = rounds + dr;
        if (candidatePlayers < 4 || candidateRounds < 1) continue;

        var candidatePlayerList = Enumerable.Range(1, candidatePlayers)
            .Select(id => new Player { Id = id, Name = $"P{id}" })
            .ToList();

        try
        {
            var result = generator.Generate(candidatePlayerList, courts, candidateRounds);
            if (result.Hr1Violations == 0 && result.Hr2Violations == 0)
            {
                return $"{candidatePlayers} players and {candidateRounds} rounds";
            }
        }
        catch { /* skip infeasible configs */ }
    }
    return null;
}
```

- [ ] **Step 4: Run — expect PASS**

- [ ] **Step 5: Commit**

```
git add PickleballScheduler/Services/ScheduleGenerator.cs \
  PickleballScheduler.Tests/Services/ScheduleGeneratorTests.cs
git commit -m "feat: add TrySuggestZeroViolationConfig"
```

---

## Task 12: Wire `EventSetup.razor` to compute suggestion and persist counts

**Files:**
- Modify: `PickleballScheduler/Components/Pages/EventSetup.razor`
- Modify: `PickleballScheduler/Services/EventService.cs` (delete obsolete overload)

- [ ] **Step 1: Replace the generation block**

In `EventSetup.razor` around line 320, change:

```csharp
var result = ScheduleGenerator.Generate(dbPlayers, numberOfCourts, numberOfRounds);
await EventService.SaveScheduleAsync(evt.Id, result.Rounds);
```

to:

```csharp
var result = ScheduleGenerator.Generate(dbPlayers, numberOfCourts, numberOfRounds);
string? suggestion = null;
if (result.Hr1Violations > 0 || result.Hr2Violations > 0)
{
    suggestion = ScheduleGenerator.TrySuggestZeroViolationConfig(dbPlayers, numberOfCourts, numberOfRounds);
}
var resultWithSuggestion = result with { RepeatSuggestion = suggestion };
await EventService.SaveScheduleAsync(evt.Id, resultWithSuggestion);
```

- [ ] **Step 2: Delete the obsolete `SaveScheduleAsync(int, List<Round>)` overload**

In `PickleballScheduler/Services/EventService.cs`, delete the original `SaveScheduleAsync(int eventId, List<Round> rounds)` method (the older one — the new `ScheduleResult` overload from Task 10 stays).

- [ ] **Step 3: Build and verify**

```
dotnet build
dotnet test
```

Expected: build green, all tests pass. If anything else in the project still calls the deleted overload, the build catches it now.

- [ ] **Step 4: Commit**

```
git add PickleballScheduler/Services/EventService.cs \
  PickleballScheduler/Components/Pages/EventSetup.razor
git commit -m "feat: compute repeat suggestion at gen time and remove old SaveSchedule overload"
```

---

## Task 13: Render banner on `Schedule.razor`

**Files:**
- Modify: `PickleballScheduler/Components/Pages/Schedule.razor`

- [ ] **Step 1: Insert banner above the schedule sheet**

In `PickleballScheduler/Components/Pages/Schedule.razor`, between the `no-print` button row (line 17) and the `@if (evt.Rounds.Count == 0)` check (line 19), insert:

```razor
@if (evt.Hr1Violations > 0 || evt.Hr2Violations > 0)
{
    <div class="alert alert-warning no-print" role="alert">
        ⚠ With these settings, this schedule has some unavoidable repeats:
        <strong>@evt.Hr1Violations</strong> early partner repeat(s),
        <strong>@evt.Hr2Violations</strong> back-to-back opponent matchup(s).
        @if (!string.IsNullOrEmpty(evt.RepeatSuggestion))
        {
            <text> For a perfectly fair schedule, try <strong>@evt.RepeatSuggestion</strong>.</text>
        }
    </div>
}
```

The `no-print` class is the existing convention for elements suppressed in the print stylesheet.

- [ ] **Step 2: Manual smoke test**

```
dotnet run --project PickleballScheduler
```

In a browser:
1. Create an event with **4 players, 1 court, 5 rounds** — should display the banner with HR2 violations and a suggestion.
2. Create an event with **8 players, 2 courts, 7 rounds** — should display *no* banner.
3. Print preview both — banner should not appear in print.

- [ ] **Step 3: Commit**

```
git add PickleballScheduler/Components/Pages/Schedule.razor
git commit -m "feat: show banner on Schedule when configuration forces repeats"
```

---

## Task 14: Verify Paul's case end-to-end

**Files:** none (manual verification step)

- [ ] **Step 1: Generate schedule for 8 players, 2 courts, 10 rounds**

In a browser, create an event with these exact parameters and 8 dummy player names.

- [ ] **Step 2: Verify banner is hidden**

The banner should not appear (HR1 = HR2 = 0).

- [ ] **Step 3: Inspect partner matrix**

Manually walk the schedule and confirm:
- No two consecutive rounds have the same partnership.
- No two consecutive rounds have the same matchup (team-vs-team).
- Across the 10 rounds, partner counts per player should be 1 or 2 only (max-min ≤ 1).

- [ ] **Step 4: No commit (verification only)**

---

## Task 15: Performance benchmark for 16-active and beam-search fallback

**Files:**
- Modify: `PickleballScheduler.Tests/Services/ScheduleDistributionTests.cs`
- Possibly modify: `PickleballScheduler/Services/ScheduleGenerator.cs`

- [ ] **Step 1: Add a benchmark test**

```csharp
[Fact]
public void Generate_16Players_4Courts_10Rounds_CompletesWithinBudget()
{
    var players = Enumerable.Range(1, 16)
        .Select(i => new Player { Id = i, Name = $"P{i}" })
        .ToList();
    var generator = new ScheduleGenerator();

    var sw = System.Diagnostics.Stopwatch.StartNew();
    var result = generator.Generate(players, courts: 4, rounds: 10);
    sw.Stop();

    Assert.Equal(0, result.Hr1Violations);
    Assert.Equal(0, result.Hr2Violations);
    Assert.True(sw.ElapsedMilliseconds < 5000,
        $"Generation took {sw.ElapsedMilliseconds}ms, exceeds 5s budget");
}
```

- [ ] **Step 2: Run — see whether pruning is sufficient**

```
dotnet test --filter "FullyQualifiedName~Generate_16Players_4Courts_10Rounds_CompletesWithinBudget"
```

If it passes, proceed to Step 5 (commit). If it fails on time, proceed to Step 3.

- [ ] **Step 3 (only if needed): Add a beam-search fallback**

In `ScheduleGenerator.SearchMatches`, add a beam-width parameter:

```csharp
private const int SearchBeamWidth = 32;  // 0 = exhaustive
```

In the inner partner-selection loop, sort candidates by their incremental cost contribution and consider only the top `SearchBeamWidth` per level when active player count > 12. This trades exhaustive optimality for time but typically still finds zero-violation schedules.

Concretely, replace the inner `for (int j = first + 1; j < active.Count; j++)` loop body with: collect each candidate's `(j, partnerHr1 * 1_000_000 + partnerSqDelta)` tuple, sort, take the top `SearchBeamWidth`, then iterate.

- [ ] **Step 4 (only if Step 3 needed): Re-run benchmark**

```
dotnet test --filter "FullyQualifiedName~Generate_16Players_4Courts_10Rounds_CompletesWithinBudget"
```

Tune `SearchBeamWidth` if still slow. Re-run the full distribution test suite to confirm no regressions:

```
dotnet test --filter "FullyQualifiedName~ScheduleDistributionTests"
```

- [ ] **Step 5: Commit**

```
git add PickleballScheduler.Tests/Services/ScheduleDistributionTests.cs \
  PickleballScheduler/Services/ScheduleGenerator.cs
git commit -m "test: add 16-active performance benchmark (beam search fallback if needed)"
```

---

## Self-Review Notes

- **Spec coverage:** HR1, HR2, lex tuple, joint search, persistence (3 fields + migration), banner, near-neighbor suggestion, distribution theory test, stress test, feasibility helpers, performance benchmark, beam-search fallback — all present.
- **Type consistency:** `ScheduleResult` (record) used everywhere. `CostTuple` (readonly record struct) only inside `ScheduleGenerator`. `IsHr1Violation` / `IsHr2Violation` signatures match between Task 6 (definition) and Task 7 (use). `TrySuggestZeroViolationConfig` returns `string?`.
- **Dependencies:** Task 11 depends on Task 7 (the suggestion calls Generate). Task 12 depends on Tasks 10 and 11. Task 13 depends on Task 9 (Event fields) and Task 12 (data flowing in).
- **Open question for execution:** the migration in Task 9 mutates the local SQLite database; if the dev DB has existing events, they'll get default 0/null values, which is fine for the banner (it'll be hidden).
