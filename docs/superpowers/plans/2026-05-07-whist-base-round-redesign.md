# Whist Base Round Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the canonical Whist base rounds in `WhistMatchups.cs` for sizes 8, 12, 16, 20, 24 with new base rounds that minimize the worst-case "first-coverage round" (the round by which a player has shared a court with every other player).

**Architecture:** Build a search tool inside the test project that enumerates valid Whist base rounds for a given size, validates them with difference-class analysis, scores them by coverage, and prints the best result in a paste-ready format. Run it once per size, paste outputs into `WhistMatchups.cs`, then add regression tests asserting the new invariants and coverage bounds.

**Tech Stack:** .NET 8, C#, xUnit. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-05-07-whist-base-round-redesign.md`

---

## File Structure

Create:
- `PickleballScheduler.Tests/Search/Difference.cs` — pure helpers for difference-class arithmetic.
- `PickleballScheduler.Tests/Search/BaseRoundCandidate.cs` — record type for a candidate base round.
- `PickleballScheduler.Tests/Search/WhistValidator.cs` — partner-once / opponent-twice check.
- `PickleballScheduler.Tests/Search/CoverageCalculator.cs` — first-coverage round per player.
- `PickleballScheduler.Tests/Search/BaseRoundEnumerator.cs` — symmetry-broken candidate stream.
- `PickleballScheduler.Tests/Search/SearchRunner.cs` — orchestrates enumeration → validate → score → best.
- `PickleballScheduler.Tests/Search/SearchUnitTests.cs` — fast tests for the search building blocks.
- `PickleballScheduler.Tests/Search/SearchRunnerTests.cs` — `[Fact(Skip = ...)]` tests that run the search per size and print the result. Manual unskip to run.
- `PickleballScheduler.Tests/Services/WhistMatchupsTests.cs` — production-side regression tests (invariants + coverage bounds) on the shipped base rounds.

Modify:
- `PickleballScheduler/Services/WhistMatchups.cs` — replace `BaseRounds` dictionary entries (final task).

---

## Task 1: Difference-class helper

**Files:**
- Create: `PickleballScheduler.Tests/Search/Difference.cs`
- Create: `PickleballScheduler.Tests/Search/SearchUnitTests.cs`

A "difference class" in Wh(n) is the smaller of `|a-b|` and `(n-1) - |a-b|`. There are `(n-1)/2` classes (rounded down; n-1 is always odd for canonical sizes). Used to certify Whist validity.

- [ ] **Step 1.1: Write failing test for `Difference.Class`**

In `SearchUnitTests.cs`:

```csharp
using PickleballScheduler.Tests.Search;

namespace PickleballScheduler.Tests.Search;

public class DifferenceTests
{
    [Theory]
    [InlineData(0, 1, 15, 1)]   // |0-1|=1, min(1, 14)=1
    [InlineData(0, 8, 15, 7)]   // |0-8|=8, min(8, 7)=7
    [InlineData(2, 5, 15, 3)]
    [InlineData(2, 13, 15, 4)]  // |2-13|=11, min(11, 4)=4
    [InlineData(0, 0, 15, 0)]   // same role
    public void Class_ComputesSymmetricDifference(int a, int b, int rotateMod, int expected)
    {
        Assert.Equal(expected, Difference.Class(a, b, rotateMod));
    }
}
```

- [ ] **Step 1.2: Run to verify it fails**

Run: `dotnet test PickleballScheduler.Tests --filter "FullyQualifiedName~DifferenceTests"`
Expected: build error, `Difference` does not exist.

- [ ] **Step 1.3: Implement `Difference`**

In `Difference.cs`:

```csharp
namespace PickleballScheduler.Tests.Search;

internal static class Difference
{
    /// <summary>
    /// Symmetric difference class mod rotateMod: min(|a-b|, rotateMod - |a-b|).
    /// Two pairs share a class iff one is a rotation of the other.
    /// </summary>
    public static int Class(int a, int b, int rotateMod)
    {
        int d = Math.Abs(a - b) % rotateMod;
        return Math.Min(d, rotateMod - d);
    }
}
```

- [ ] **Step 1.4: Run to verify it passes**

Run: `dotnet test PickleballScheduler.Tests --filter "FullyQualifiedName~DifferenceTests"`
Expected: PASS (5 tests).

- [ ] **Step 1.5: Commit**

```
git add PickleballScheduler.Tests/Search/Difference.cs PickleballScheduler.Tests/Search/SearchUnitTests.cs
git commit -m "test(search): add Difference.Class helper for Whist analysis"
```

---

## Task 2: BaseRoundCandidate type

**Files:**
- Create: `PickleballScheduler.Tests/Search/BaseRoundCandidate.cs`
- Modify: `PickleballScheduler.Tests/Search/SearchUnitTests.cs`

A candidate base round is `n/4` matches. The first match always contains the `inf` role (represented as `int.MinValue` to keep the type uniform with finite role IDs in `0..n-2`). Each match is a 4-tuple of role IDs grouped as `(team1a, team1b, team2a, team2b)`.

- [ ] **Step 2.1: Write failing test for round-trip and role iteration**

Append to `SearchUnitTests.cs`:

```csharp
public class BaseRoundCandidateTests
{
    [Fact]
    public void FromTuples_PreservesMatchOrder()
    {
        var c = BaseRoundCandidate.FromTuples(
            playerCount: 16,
            (BaseRoundCandidate.Inf, 0, 1, 2),
            (3, 6, 9, 11),
            (4, 13, 8, 12),
            (5, 10, 14, 7));

        Assert.Equal(16, c.PlayerCount);
        Assert.Equal(15, c.RotateMod);
        Assert.Equal(4, c.Matches.Count);
        Assert.Equal(BaseRoundCandidate.Inf, c.Matches[0].Team1A);
        Assert.Equal(7, c.Matches[3].Team2B);
    }

    [Fact]
    public void FiniteRoles_EnumeratesAllNonInfRoles()
    {
        var c = BaseRoundCandidate.FromTuples(
            playerCount: 8,
            (BaseRoundCandidate.Inf, 0, 1, 3),
            (2, 6, 4, 5));

        var finite = c.FiniteRoles().OrderBy(r => r).ToArray();
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 }, finite);
    }
}
```

- [ ] **Step 2.2: Run to verify it fails**

Run: `dotnet test PickleballScheduler.Tests --filter "FullyQualifiedName~BaseRoundCandidateTests"`
Expected: build error.

- [ ] **Step 2.3: Implement `BaseRoundCandidate`**

In `BaseRoundCandidate.cs`:

```csharp
namespace PickleballScheduler.Tests.Search;

/// <summary>
/// Candidate Whist base round for size <see cref="PlayerCount"/>.
/// Match[0] always contains the inf role; remaining matches are finite-only.
/// Roles 0..PlayerCount-2 rotate; inf is fixed.
/// </summary>
internal sealed record BaseRoundCandidate(int PlayerCount, IReadOnlyList<BaseRoundCandidate.Match> Matches)
{
    public const int Inf = int.MinValue;

    public int RotateMod => PlayerCount - 1;

    public IEnumerable<int> FiniteRoles() =>
        Matches.SelectMany(m => new[] { m.Team1A, m.Team1B, m.Team2A, m.Team2B })
               .Where(r => r != Inf);

    public static BaseRoundCandidate FromTuples(int playerCount, params (int a, int b, int c, int d)[] matches)
    {
        var list = matches.Select(t => new Match(t.a, t.b, t.c, t.d)).ToList();
        return new BaseRoundCandidate(playerCount, list);
    }

    public sealed record Match(int Team1A, int Team1B, int Team2A, int Team2B);
}
```

- [ ] **Step 2.4: Run to verify it passes**

Run: `dotnet test PickleballScheduler.Tests --filter "FullyQualifiedName~BaseRoundCandidateTests"`
Expected: PASS (2 tests).

- [ ] **Step 2.5: Commit**

```
git add PickleballScheduler.Tests/Search/BaseRoundCandidate.cs PickleballScheduler.Tests/Search/SearchUnitTests.cs
git commit -m "test(search): add BaseRoundCandidate record type"
```

---

## Task 3: WhistValidator

**Files:**
- Create: `PickleballScheduler.Tests/Search/WhistValidator.cs`
- Modify: `PickleballScheduler.Tests/Search/SearchUnitTests.cs`

Validates that a candidate forms a valid Whist tournament when rotated `n-1` times.

Rules (for canonical sizes where exactly one match contains inf):
1. The role set is exactly `{Inf} ∪ {0..n-2}`, no duplicates.
2. Finite partner-pair difference classes form a permutation of `{1, 2, ..., (n-1)/2}` (each class hit exactly once).
3. Finite opponent-pair difference classes hit each value in `{1, 2, ..., (n-1)/2}` exactly twice.

- [ ] **Step 3.1: Write failing tests for validator**

Append to `SearchUnitTests.cs`:

```csharp
public class WhistValidatorTests
{
    // Existing canonical Wh(8): (inf,0,1,3 | 2,6,4,5) is valid.
    [Fact]
    public void IsValid_ExistingWh8_ReturnsTrue()
    {
        var c = BaseRoundCandidate.FromTuples(8,
            (BaseRoundCandidate.Inf, 0, 1, 3),
            (2, 6, 4, 5));
        Assert.True(WhistValidator.IsValid(c, out _));
    }

    [Fact]
    public void IsValid_ExistingWh16_ReturnsTrue()
    {
        var c = BaseRoundCandidate.FromTuples(16,
            (BaseRoundCandidate.Inf, 0, 1, 2),
            (3, 6, 9, 11),
            (4, 13, 8, 12),
            (5, 10, 14, 7));
        Assert.True(WhistValidator.IsValid(c, out _));
    }

    [Fact]
    public void IsValid_DuplicateRole_ReturnsFalse()
    {
        var c = BaseRoundCandidate.FromTuples(8,
            (BaseRoundCandidate.Inf, 0, 1, 1),  // role 1 duplicated
            (2, 6, 4, 5));
        Assert.False(WhistValidator.IsValid(c, out var reason));
        Assert.Contains("duplicate", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsValid_PartnerDifferenceCollision_ReturnsFalse()
    {
        // Force two finite partner pairs to share a difference class, breaking partner-once.
        // Wh(8): partner classes must be {1,2,3}. Use (inf,0,1,2 | 3,4,5,6) where finite pairs
        // (1,2) diff=1 and (5,6) diff=1 collide.
        var c = BaseRoundCandidate.FromTuples(8,
            (BaseRoundCandidate.Inf, 0, 1, 2),
            (3, 4, 5, 6));
        Assert.False(WhistValidator.IsValid(c, out _));
    }
}
```

- [ ] **Step 3.2: Run to verify they fail**

Run: `dotnet test PickleballScheduler.Tests --filter "FullyQualifiedName~WhistValidatorTests"`
Expected: build error.

- [ ] **Step 3.3: Implement `WhistValidator`**

In `WhistValidator.cs`:

```csharp
namespace PickleballScheduler.Tests.Search;

internal static class WhistValidator
{
    /// <summary>
    /// Returns true iff the candidate forms a valid Whist tournament when rotated n-1 times:
    /// every pair partners exactly once, every pair opposes exactly twice. Failure reason
    /// is set when false.
    /// </summary>
    public static bool IsValid(BaseRoundCandidate c, out string reason)
    {
        var n = c.PlayerCount;
        var rotateMod = c.RotateMod;
        var maxClass = rotateMod / 2;

        // 1. Role set is exactly {Inf} ∪ {0..n-2}.
        var seen = new HashSet<int>();
        int infCount = 0;
        foreach (var m in c.Matches)
        {
            foreach (var r in new[] { m.Team1A, m.Team1B, m.Team2A, m.Team2B })
            {
                if (r == BaseRoundCandidate.Inf) { infCount++; continue; }
                if (r < 0 || r >= rotateMod) { reason = $"role {r} out of range"; return false; }
                if (!seen.Add(r)) { reason = $"duplicate role {r}"; return false; }
            }
        }
        if (infCount != 1) { reason = $"expected 1 inf, got {infCount}"; return false; }
        if (seen.Count != rotateMod) { reason = $"expected {rotateMod} finite roles, got {seen.Count}"; return false; }

        // 2. Finite partner-pair difference classes form a permutation of {1..maxClass}.
        var partnerClasses = new int[maxClass + 1];
        foreach (var m in c.Matches)
        {
            CountPartnerPair(m.Team1A, m.Team1B, rotateMod, partnerClasses);
            CountPartnerPair(m.Team2A, m.Team2B, rotateMod, partnerClasses);
        }
        for (int k = 1; k <= maxClass; k++)
        {
            if (partnerClasses[k] != 1)
            {
                reason = $"partner difference class {k} hit {partnerClasses[k]} times, expected 1";
                return false;
            }
        }

        // 3. Finite opponent-pair difference classes hit each {1..maxClass} exactly twice.
        var opponentClasses = new int[maxClass + 1];
        foreach (var m in c.Matches)
        {
            CountOpponentPair(m.Team1A, m.Team2A, rotateMod, opponentClasses);
            CountOpponentPair(m.Team1A, m.Team2B, rotateMod, opponentClasses);
            CountOpponentPair(m.Team1B, m.Team2A, rotateMod, opponentClasses);
            CountOpponentPair(m.Team1B, m.Team2B, rotateMod, opponentClasses);
        }
        for (int k = 1; k <= maxClass; k++)
        {
            if (opponentClasses[k] != 2)
            {
                reason = $"opponent difference class {k} hit {opponentClasses[k]} times, expected 2";
                return false;
            }
        }

        reason = "";
        return true;
    }

    private static void CountPartnerPair(int a, int b, int rotateMod, int[] classes)
    {
        if (a == BaseRoundCandidate.Inf || b == BaseRoundCandidate.Inf) return;
        var k = Difference.Class(a, b, rotateMod);
        if (k > 0 && k < classes.Length) classes[k]++;
    }

    private static void CountOpponentPair(int a, int b, int rotateMod, int[] classes)
    {
        if (a == BaseRoundCandidate.Inf || b == BaseRoundCandidate.Inf) return;
        var k = Difference.Class(a, b, rotateMod);
        if (k > 0 && k < classes.Length) classes[k]++;
    }
}
```

- [ ] **Step 3.4: Run to verify tests pass**

Run: `dotnet test PickleballScheduler.Tests --filter "FullyQualifiedName~WhistValidatorTests"`
Expected: PASS (4 tests).

- [ ] **Step 3.5: Add tests confirming all currently-shipped base rounds validate**

Append to `WhistValidatorTests`:

```csharp
[Theory]
[InlineData(8)]
[InlineData(12)]
[InlineData(16)]
[InlineData(20)]
[InlineData(24)]
public void IsValid_AllShippedBaseRounds_ReturnTrue(int n)
{
    var shipped = TestData.ShippedBaseRound(n);
    Assert.True(WhistValidator.IsValid(shipped, out var reason),
        $"n={n} shipped base round failed validation: {reason}");
}
```

- [ ] **Step 3.6: Create `TestData` helper holding the currently-shipped base rounds**

Create `PickleballScheduler.Tests/Search/TestData.cs`:

```csharp
namespace PickleballScheduler.Tests.Search;

internal static class TestData
{
    /// <summary>
    /// Snapshot of the base rounds shipped in WhistMatchups before the redesign.
    /// Used to (a) verify the validator agrees they are valid Whist, and
    /// (b) compare baseline coverage before/after.
    /// </summary>
    public static BaseRoundCandidate ShippedBaseRound(int n) => n switch
    {
        8 => BaseRoundCandidate.FromTuples(8,
            (BaseRoundCandidate.Inf, 0, 1, 3),
            (2, 6, 4, 5)),
        12 => BaseRoundCandidate.FromTuples(12,
            (BaseRoundCandidate.Inf, 0, 1, 3),
            (2, 9, 6, 7),
            (4, 10, 5, 8)),
        16 => BaseRoundCandidate.FromTuples(16,
            (BaseRoundCandidate.Inf, 0, 1, 2),
            (3, 6, 9, 11),
            (4, 13, 8, 12),
            (5, 10, 14, 7)),
        20 => BaseRoundCandidate.FromTuples(20,
            (BaseRoundCandidate.Inf, 0, 1, 2),
            (3, 5, 9, 15),
            (4, 14, 17, 12),
            (6, 18, 10, 13),
            (7, 11, 16, 8)),
        24 => BaseRoundCandidate.FromTuples(24,
            (BaseRoundCandidate.Inf, 0, 1, 2),
            (3, 14, 4, 7),
            (5, 20, 12, 17),
            (6, 15, 21, 11),
            (8, 10, 13, 19),
            (9, 16, 18, 22)),
        _ => throw new ArgumentOutOfRangeException(nameof(n))
    };
}
```

- [ ] **Step 3.7: Run new tests**

Run: `dotnet test PickleballScheduler.Tests --filter "FullyQualifiedName~WhistValidatorTests"`
Expected: PASS (9 tests). If any shipped base round fails, the validator has a bug — fix it before continuing.

- [ ] **Step 3.8: Commit**

```
git add PickleballScheduler.Tests/Search/WhistValidator.cs PickleballScheduler.Tests/Search/TestData.cs PickleballScheduler.Tests/Search/SearchUnitTests.cs
git commit -m "test(search): add WhistValidator with difference-class checks"
```

---

## Task 4: CoverageCalculator

**Files:**
- Create: `PickleballScheduler.Tests/Search/CoverageCalculator.cs`
- Modify: `PickleballScheduler.Tests/Search/SearchUnitTests.cs`

Computes, for each player, the smallest round R such that across rounds 0..R the player has shared a court with all other n-1 players. Returns max and per-player array.

- [ ] **Step 4.1: Write failing tests using known coverage of shipped Wh(16) and Wh(8)**

Append to `SearchUnitTests.cs`:

```csharp
public class CoverageCalculatorTests
{
    [Fact]
    public void MaxCoverageRound_ShippedWh16_IsThirteen()
    {
        // P0 (inf) takes 13 rounds (round index 12) to meet all 15 others under (inf,0,1,2).
        var c = TestData.ShippedBaseRound(16);
        var result = CoverageCalculator.Compute(c);
        Assert.Equal(12, result.MaxCoverageRound);  // 0-indexed round index
    }

    [Fact]
    public void MaxCoverageRound_ShippedWh8_IsThree()
    {
        // P0 takes 4 rounds (round index 3) to meet all 7 others under (inf,0,1,3).
        var c = TestData.ShippedBaseRound(8);
        var result = CoverageCalculator.Compute(c);
        Assert.Equal(3, result.MaxCoverageRound);
    }

    [Fact]
    public void PerPlayerCoverage_ShippedWh16_InfPlayerIsBottleneck()
    {
        var c = TestData.ShippedBaseRound(16);
        var result = CoverageCalculator.Compute(c);
        // P0 (player ID 0 in our indexing) is the inf player and has the worst coverage.
        Assert.Equal(12, result.CoverageRoundByPlayer[0]);
        // Other players cycle through diverse matches; their coverage should be strictly better.
        for (int p = 1; p < 16; p++)
            Assert.True(result.CoverageRoundByPlayer[p] < 12,
                $"P{p} coverage {result.CoverageRoundByPlayer[p]} should be < inf-player's 12");
    }
}
```

- [ ] **Step 4.2: Run to verify they fail**

Run: `dotnet test PickleballScheduler.Tests --filter "FullyQualifiedName~CoverageCalculatorTests"`
Expected: build error.

- [ ] **Step 4.3: Implement `CoverageCalculator`**

In `CoverageCalculator.cs`:

```csharp
namespace PickleballScheduler.Tests.Search;

internal static class CoverageCalculator
{
    public sealed record Result(int MaxCoverageRound, int SumCoverageRound, int[] CoverageRoundByPlayer);

    /// <summary>
    /// Simulates n-1 rounds of the Whist schedule generated by rotating the candidate.
    /// For each player p, returns the smallest 0-indexed round R such that across rounds 0..R,
    /// p has shared a court with every other player. If a player never reaches full coverage
    /// in n-1 rounds, that's a fatal validity issue — assertion fails.
    /// </summary>
    public static Result Compute(BaseRoundCandidate c)
    {
        int n = c.PlayerCount;
        int rotateMod = c.RotateMod;
        int rounds = rotateMod;  // n-1 rounds in canonical Whist cycle

        // Player IDs: 0 is inf (always P0). Finite players are 1..n-1, where finite role i in
        // round r resolves to player ((i + r) mod rotateMod) + 1.

        var met = new HashSet<int>[n];
        for (int p = 0; p < n; p++) met[p] = new HashSet<int>();

        var coverage = new int[n];
        for (int p = 0; p < n; p++) coverage[p] = -1;

        int target = n - 1;

        for (int r = 0; r < rounds; r++)
        {
            foreach (var m in c.Matches)
            {
                int pa = ResolvePlayer(m.Team1A, r, rotateMod);
                int pb = ResolvePlayer(m.Team1B, r, rotateMod);
                int pc = ResolvePlayer(m.Team2A, r, rotateMod);
                int pd = ResolvePlayer(m.Team2B, r, rotateMod);

                Span<int> court = stackalloc int[] { pa, pb, pc, pd };
                for (int i = 0; i < 4; i++)
                    for (int j = 0; j < 4; j++)
                        if (i != j) met[court[i]].Add(court[j]);
            }

            for (int p = 0; p < n; p++)
                if (coverage[p] < 0 && met[p].Count == target)
                    coverage[p] = r;
        }

        int max = 0, sum = 0;
        for (int p = 0; p < n; p++)
        {
            if (coverage[p] < 0)
                throw new InvalidOperationException(
                    $"player {p} did not reach coverage in {rounds} rounds (met {met[p].Count}/{target}) — base round invalid");
            if (coverage[p] > max) max = coverage[p];
            sum += coverage[p];
        }
        return new Result(max, sum, coverage);
    }

    private static int ResolvePlayer(int role, int round, int rotateMod)
    {
        if (role == BaseRoundCandidate.Inf) return 0;
        return ((role + round) % rotateMod) + 1;
    }
}
```

- [ ] **Step 4.4: Run to verify tests pass**

Run: `dotnet test PickleballScheduler.Tests --filter "FullyQualifiedName~CoverageCalculatorTests"`
Expected: PASS (3 tests).

- [ ] **Step 4.5: Commit**

```
git add PickleballScheduler.Tests/Search/CoverageCalculator.cs PickleballScheduler.Tests/Search/SearchUnitTests.cs
git commit -m "test(search): add CoverageCalculator with per-player first-coverage round"
```

---

## Task 5: BaseRoundEnumerator with symmetry breaking

**Files:**
- Create: `PickleballScheduler.Tests/Search/BaseRoundEnumerator.cs`
- Modify: `PickleballScheduler.Tests/Search/SearchUnitTests.cs`

Generates candidate base rounds for size n with these symmetry-breaking conventions:
1. Match[0] always contains inf as Team1A.
2. Match[0]'s inf-partner (Team1B) is the smallest finite role in match[0].
3. Within each non-inf match, Team1's smaller role comes first (Team1A < Team1B), Team2's smaller role comes first (Team2A < Team2B), and Team1A < Team2A.
4. Non-inf matches are ordered by Team1A ascending.

These conventions reduce duplicates without losing any valid distinct Whist designs.

- [ ] **Step 5.1: Write failing test for n=8 enumeration count and structure**

Append to `SearchUnitTests.cs`:

```csharp
public class BaseRoundEnumeratorTests
{
    [Fact]
    public void Enumerate_n8_ProducesAtLeastOneValidCandidate()
    {
        var validCount = 0;
        foreach (var candidate in BaseRoundEnumerator.Enumerate(8))
        {
            if (WhistValidator.IsValid(candidate, out _)) validCount++;
        }
        Assert.True(validCount > 0, "expected at least one valid Wh(8) candidate");
    }

    [Fact]
    public void Enumerate_n8_AllRespectSymmetryBreaking()
    {
        foreach (var c in BaseRoundEnumerator.Enumerate(8))
        {
            var m0 = c.Matches[0];
            Assert.Equal(BaseRoundCandidate.Inf, m0.Team1A);

            // Match[0]'s inf-partner is the smallest finite role in match[0].
            var m0Finites = new[] { m0.Team1B, m0.Team2A, m0.Team2B };
            Assert.Equal(m0Finites.Min(), m0.Team1B);

            // Non-inf matches: team1A < team1B, team2A < team2B, team1A < team2A.
            for (int i = 1; i < c.Matches.Count; i++)
            {
                var m = c.Matches[i];
                Assert.True(m.Team1A < m.Team1B);
                Assert.True(m.Team2A < m.Team2B);
                Assert.True(m.Team1A < m.Team2A);
            }

            // Non-inf matches sorted by Team1A ascending.
            for (int i = 2; i < c.Matches.Count; i++)
                Assert.True(c.Matches[i - 1].Team1A < c.Matches[i].Team1A);
        }
    }
}
```

- [ ] **Step 5.2: Run to verify it fails**

Run: `dotnet test PickleballScheduler.Tests --filter "FullyQualifiedName~BaseRoundEnumeratorTests"`
Expected: build error.

- [ ] **Step 5.3: Implement `BaseRoundEnumerator`**

In `BaseRoundEnumerator.cs`:

```csharp
namespace PickleballScheduler.Tests.Search;

internal static class BaseRoundEnumerator
{
    /// <summary>
    /// Yields candidate base rounds for size n with symmetry-breaking applied.
    /// Does NOT pre-filter for Whist validity — caller must run WhistValidator.
    /// </summary>
    public static IEnumerable<BaseRoundCandidate> Enumerate(int n)
    {
        if (n % 4 != 0) throw new ArgumentException("n must be divisible by 4", nameof(n));
        if (!new[] { 8, 12, 16, 20, 24 }.Contains(n))
            throw new ArgumentException($"unsupported size {n}", nameof(n));

        int rotateMod = n - 1;
        int matchCount = n / 4;

        // Pick 3 finite roles for match[0]; smallest becomes inf-partner (team1B).
        var finiteRoles = Enumerable.Range(0, rotateMod).ToArray();
        for (int a = 0; a < rotateMod; a++)
        for (int b = a + 1; b < rotateMod; b++)
        for (int c = b + 1; c < rotateMod; c++)
        {
            // Match[0]: inf, a (partner), then opponents b, c (sorted).
            // Team2 within match[0] has team2A < team2B, but we also need team1A=inf < team2A
            // which is automatic since inf is sentinel min.
            var m0 = new BaseRoundCandidate.Match(BaseRoundCandidate.Inf, a, b, c);

            var remaining = finiteRoles.Where(r => r != a && r != b && r != c).ToArray();
            foreach (var rest in PartitionIntoMatches(remaining))
            {
                var allMatches = new List<BaseRoundCandidate.Match> { m0 };
                allMatches.AddRange(rest);
                yield return new BaseRoundCandidate(n, allMatches);
            }
        }
    }

    /// <summary>
    /// Partitions the remaining finite roles into matches of 4, then teams within each match,
    /// applying symmetry-breaking. Yields each valid partition once.
    /// </summary>
    private static IEnumerable<List<BaseRoundCandidate.Match>> PartitionIntoMatches(int[] roles)
    {
        if (roles.Length == 0) { yield return new List<BaseRoundCandidate.Match>(); yield break; }
        if (roles.Length % 4 != 0) yield break;

        // Anchor: smallest remaining role is always team1A of the first remaining match.
        int anchor = roles[0];
        var pool = roles.Skip(1).ToArray();

        // Choose team1B (any of the rest), then team2A and team2B (smaller-first, with team1A < team2A
        // already enforced because anchor is the global min).
        for (int i1B = 0; i1B < pool.Length; i1B++)
        for (int i2A = 0; i2A < pool.Length; i2A++)
        {
            if (i2A == i1B) continue;
            for (int i2B = i2A + 1; i2B < pool.Length; i2B++)
            {
                if (i2B == i1B) continue;
                int t1B = pool[i1B], t2A = pool[i2A], t2B = pool[i2B];
                // team1A < team1B
                if (anchor >= t1B) continue;
                // team2A < team2B already enforced by i2B > i2A
                // team1A < team2A already enforced (anchor is global min of roles, t2A != anchor)
                if (anchor >= t2A) continue;

                var match = new BaseRoundCandidate.Match(anchor, t1B, t2A, t2B);

                var leftover = pool.Where((_, idx) => idx != i1B && idx != i2A && idx != i2B).ToArray();
                foreach (var rest in PartitionIntoMatches(leftover))
                {
                    var combined = new List<BaseRoundCandidate.Match> { match };
                    combined.AddRange(rest);
                    yield return combined;
                }
            }
        }
    }
}
```

- [ ] **Step 5.4: Run to verify tests pass**

Run: `dotnet test PickleballScheduler.Tests --filter "FullyQualifiedName~BaseRoundEnumeratorTests"`
Expected: PASS (2 tests).

- [ ] **Step 5.5: Add a quick sanity test that the existing Wh(16) is among the enumerated candidates**

Append to `BaseRoundEnumeratorTests`:

```csharp
[Fact]
public void Enumerate_n16_IncludesShippedBaseRound()
{
    var shipped = TestData.ShippedBaseRound(16);
    bool found = false;
    foreach (var candidate in BaseRoundEnumerator.Enumerate(16))
    {
        if (CandidatesEqual(candidate, shipped)) { found = true; break; }
    }
    Assert.True(found, "shipped Wh(16) should appear in enumerated candidates");
}

private static bool CandidatesEqual(BaseRoundCandidate a, BaseRoundCandidate b)
{
    if (a.PlayerCount != b.PlayerCount || a.Matches.Count != b.Matches.Count) return false;
    for (int i = 0; i < a.Matches.Count; i++)
    {
        var ma = a.Matches[i];
        var mb = b.Matches[i];
        if (ma.Team1A != mb.Team1A || ma.Team1B != mb.Team1B
         || ma.Team2A != mb.Team2A || ma.Team2B != mb.Team2B) return false;
    }
    return true;
}
```

> NOTE: the shipped Wh(16) match[1] = `(3, 6, 9, 11)` has team1A=3, team1B=6, team2A=9, team2B=11 — passes symmetry-breaking. Same for matches[2,3] after applying ordering conventions. If `Enumerate_n16_IncludesShippedBaseRound` fails, the shipped data may need re-sorting to canonical form before the lookup; if so, normalize the shipped tuples in `TestData.ShippedBaseRound` to the symmetry-broken form (sort within team, sort teams, sort matches) and adjust this assertion.

- [ ] **Step 5.6: Run; if test fails, normalize TestData**

Run: `dotnet test PickleballScheduler.Tests --filter "FullyQualifiedName~Enumerate_n16_IncludesShippedBaseRound"`
Expected: PASS. If it fails, update `TestData.ShippedBaseRound(16)` to write each match in symmetry-broken form, e.g. swap teams or reorder roles within a team.

- [ ] **Step 5.7: Commit**

```
git add PickleballScheduler.Tests/Search/BaseRoundEnumerator.cs PickleballScheduler.Tests/Search/SearchUnitTests.cs PickleballScheduler.Tests/Search/TestData.cs
git commit -m "test(search): add BaseRoundEnumerator with symmetry breaking"
```

---

## Task 6: SearchRunner — orchestrate and run search

**Files:**
- Create: `PickleballScheduler.Tests/Search/SearchRunner.cs`
- Create: `PickleballScheduler.Tests/Search/SearchRunnerTests.cs`

Wires enumeration → validate → score → best. Tests are gated with `Skip` so they don't run in CI; they're invoked manually to produce the optimal base round per size and print it.

- [ ] **Step 6.1: Write a fast test for n=8 (small enough to run in CI)**

In `SearchRunnerTests.cs`:

```csharp
using Xunit.Abstractions;

namespace PickleballScheduler.Tests.Search;

public class SearchRunnerTests
{
    private readonly ITestOutputHelper _output;
    public SearchRunnerTests(ITestOutputHelper output) { _output = output; }

    [Fact]
    public void Find_n8_ReturnsValidCandidateNoWorseThanShipped()
    {
        var shipped = TestData.ShippedBaseRound(8);
        var shippedCoverage = CoverageCalculator.Compute(shipped).MaxCoverageRound;

        var best = SearchRunner.FindBest(8);
        Assert.NotNull(best);

        Assert.True(WhistValidator.IsValid(best!.Candidate, out _));
        Assert.True(best.MaxCoverageRound <= shippedCoverage,
            $"search found {best.MaxCoverageRound}, expected <= shipped {shippedCoverage}");

        _output.WriteLine($"Wh(8) best max-coverage round (0-indexed): {best.MaxCoverageRound}");
        _output.WriteLine(FormatForPaste(best.Candidate));
    }

    private static string FormatForPaste(BaseRoundCandidate c)
    {
        var lines = c.Matches.Select(m =>
        {
            string F(int r) => r == BaseRoundCandidate.Inf ? "\"inf\"" : $"\"{r}\"";
            return $"    new BaseMatch({F(m.Team1A)}, {F(m.Team1B)}, {F(m.Team2A)}, {F(m.Team2B)}),";
        });
        return $"[{c.PlayerCount}] = new[]\n{{\n{string.Join("\n", lines)}\n}},";
    }
}
```

- [ ] **Step 6.2: Run to verify it fails**

Run: `dotnet test PickleballScheduler.Tests --filter "FullyQualifiedName~Find_n8"`
Expected: build error.

- [ ] **Step 6.3: Implement `SearchRunner`**

In `SearchRunner.cs`:

```csharp
namespace PickleballScheduler.Tests.Search;

internal static class SearchRunner
{
    public sealed record Best(BaseRoundCandidate Candidate, int MaxCoverageRound, int SumCoverageRound);

    /// <summary>
    /// Enumerates all symmetry-broken candidates for size n, validates each, scores by max
    /// coverage round (tiebreak by sum). Returns the best, or null if no valid candidate found.
    /// </summary>
    public static Best? FindBest(int n)
    {
        Best? best = null;
        foreach (var candidate in BaseRoundEnumerator.Enumerate(n))
        {
            if (!WhistValidator.IsValid(candidate, out _)) continue;
            var cov = CoverageCalculator.Compute(candidate);
            if (best == null
                || cov.MaxCoverageRound < best.MaxCoverageRound
                || (cov.MaxCoverageRound == best.MaxCoverageRound && cov.SumCoverageRound < best.SumCoverageRound))
            {
                best = new Best(candidate, cov.MaxCoverageRound, cov.SumCoverageRound);
            }
        }
        return best;
    }
}
```

- [ ] **Step 6.4: Run the n=8 test**

Run: `dotnet test PickleballScheduler.Tests --filter "FullyQualifiedName~Find_n8" --logger "console;verbosity=detailed"`
Expected: PASS. The output prints the best Wh(8) candidate. Note the printed `[8] = new[] { ... },` block — we'll paste it in Task 8.

- [ ] **Step 6.5: Add Skip-gated tests for n=12, 16, 20, 24**

Append to `SearchRunnerTests.cs`:

```csharp
[Theory(Skip = "Manual run: unskip to regenerate base rounds. Can take minutes for large n.")]
[InlineData(12)]
[InlineData(16)]
[InlineData(20)]
[InlineData(24)]
public void Find_n_PrintsBestForPaste(int n)
{
    var shipped = TestData.ShippedBaseRound(n);
    var shippedCoverage = CoverageCalculator.Compute(shipped).MaxCoverageRound;

    var best = SearchRunner.FindBest(n);
    Assert.NotNull(best);

    Assert.True(WhistValidator.IsValid(best!.Candidate, out _));
    _output.WriteLine($"Wh({n}) best max-coverage round (0-indexed): {best.MaxCoverageRound}");
    _output.WriteLine($"Wh({n}) shipped max-coverage round (0-indexed): {shippedCoverage}");
    _output.WriteLine($"Improvement: {shippedCoverage - best.MaxCoverageRound} rounds");
    _output.WriteLine("");
    _output.WriteLine(FormatForPaste(best.Candidate));
}
```

- [ ] **Step 6.6: Commit**

```
git add PickleballScheduler.Tests/Search/SearchRunner.cs PickleballScheduler.Tests/Search/SearchRunnerTests.cs
git commit -m "test(search): add SearchRunner with paste-ready output"
```

---

## Task 7: Run the search and capture results

The search results inform Task 8 (paste into `WhistMatchups.cs`). This task is the manual run — you'll edit `Skip` to `null`, run, capture output, and revert `Skip`.

> NOTE for the implementer: For n=20 and n=24 the search may take several minutes or longer. If a size is too slow, see "Search performance contingency" at the bottom of this plan for alternative approaches. Do not skip this task — capture results before continuing.

- [ ] **Step 7.1: Run n=12 search**

Edit `SearchRunnerTests.cs`: change `[Theory(Skip = "...")]` to `[Theory]` temporarily.

Run: `dotnet test PickleballScheduler.Tests --filter "Find_n_PrintsBestForPaste(n: 12)" --logger "console;verbosity=detailed"`

Capture the printed `[12] = new[] { ... }` block, the best max-coverage round, and the shipped baseline. Save these somewhere local (a scratch file).

- [ ] **Step 7.2: Run n=16 search**

Same approach for n=16. Capture results.

- [ ] **Step 7.3: Run n=20 search**

Same approach for n=20. Capture results.

> If runtime exceeds ~10 minutes, abort and consult the contingency section.

- [ ] **Step 7.4: Run n=24 search**

Same approach for n=24. Capture results.

> Same caveat. Capture or escalate to contingency.

- [ ] **Step 7.5: Restore the `Skip` attribute**

Re-add `Skip = "Manual run: unskip to regenerate base rounds. Can take minutes for large n."` so CI does not run it.

- [ ] **Step 7.6: Commit nothing yet — results are pasted in Task 8**

(The test file should be back to its original `Skip`-gated state. No commit needed if `git diff` shows no changes; if you accidentally left changes, revert them.)

---

## Task 8: Update WhistMatchups.cs with new base rounds

**Files:**
- Modify: `PickleballScheduler/Services/WhistMatchups.cs`

Replace the `BaseRounds` dictionary entries with the captured results from Task 7. Wh(8) value is from Task 6's `Find_n8` test output.

- [ ] **Step 8.1: Open `WhistMatchups.cs` and update `BaseRounds`**

Replace lines 56–99 (the `BaseRounds` dictionary) with the new captured base rounds. Format:

```csharp
private static readonly IReadOnlyDictionary<int, BaseMatch[]> BaseRounds =
    new Dictionary<int, BaseMatch[]>
    {
        // Wh(8): players inf, 0..6. Rotation mod 7. Selected by base-round redesign 2026-05-07
        // for minimum max first-coverage round (see docs/superpowers/specs/2026-05-07-...).
        [8] = new[]
        {
            // PASTE Task 7 (n=8) result here
        },
        [12] = new[]
        {
            // PASTE Task 7 (n=12) result here
        },
        [16] = new[]
        {
            // PASTE Task 7 (n=16) result here
        },
        [20] = new[]
        {
            // PASTE Task 7 (n=20) result here
        },
        [24] = new[]
        {
            // PASTE Task 7 (n=24) result here
        },
    };
```

- [ ] **Step 8.2: Build the solution**

Run: `dotnet build PickleballScheduler.sln`
Expected: success.

- [ ] **Step 8.3: Run the existing test suite to catch regressions**

Run: `dotnet test PickleballScheduler.sln`
Expected: All tests pass. Pay attention to:
- `ScheduleGeneratorTests` (8-player coverage assertions, court-spread bounds) — should pass; these test invariants, not specific schedules.
- `ScheduleDistributionTests` — 50 randomized configs; should pass.

If any test fails, investigate. Do not paper over with `Skip`.

- [ ] **Step 8.4: Commit**

```
git add PickleballScheduler/Services/WhistMatchups.cs
git commit -m "feat(whist): replace base rounds with redesigned versions for fast court coverage"
```

---

## Task 9: Production regression tests for shipped base rounds

**Files:**
- Create: `PickleballScheduler.Tests/Services/WhistMatchupsTests.cs`

Asserts that the shipped base rounds (a) form valid Whist tournaments and (b) achieve the max-coverage rounds we found in Task 7.

> NOTE: Fill in the `expectedMaxCoverageRound` values from Task 7 results. These are the "hard floor" we promise to keep on or below — if a future change regresses coverage, this test fails.

- [ ] **Step 9.1: Write the regression test**

In `WhistMatchupsTests.cs`:

```csharp
using PickleballScheduler.Models;
using PickleballScheduler.Services;
using PickleballScheduler.Tests.Search;

namespace PickleballScheduler.Tests.Services;

public class WhistMatchupsTests
{
    public static TheoryData<int, int> ExpectedCoverageRounds()
    {
        // 0-indexed worst-case round at which the slowest player has shared a court with
        // everyone else. Captured from the base-round search on 2026-05-07.
        // Update if a future search improves these.
        var d = new TheoryData<int, int>();
        d.Add(8, /* fill from Task 7 (n=8) */ -1);
        d.Add(12, /* fill from Task 7 (n=12) */ -1);
        d.Add(16, /* fill from Task 7 (n=16) */ -1);
        d.Add(20, /* fill from Task 7 (n=20) */ -1);
        d.Add(24, /* fill from Task 7 (n=24) */ -1);
        return d;
    }

    [Theory]
    [MemberData(nameof(ExpectedCoverageRounds))]
    public void ShippedBaseRound_HasExpectedMaxCoverage(int n, int expectedMaxCoverageRound)
    {
        Assert.NotEqual(-1, expectedMaxCoverageRound);  // sentinel: must be filled in

        var players = Enumerable.Range(1, n)
            .Select(i => new Player { Id = i, Name = $"P{i}" })
            .ToList();

        // Reconstruct the base round via the Whist API and feed it to CoverageCalculator
        // by reading match player IDs. Player[0].Id == 1 (inf), so build the candidate
        // by mapping back to roles.
        var candidate = ReconstructBaseRoundFromShipped(n, players);
        var result = CoverageCalculator.Compute(candidate);

        Assert.Equal(expectedMaxCoverageRound, result.MaxCoverageRound);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    public void ShippedBaseRound_PassesWhistValidator(int n)
    {
        var players = Enumerable.Range(1, n)
            .Select(i => new Player { Id = i, Name = $"P{i}" })
            .ToList();
        var candidate = ReconstructBaseRoundFromShipped(n, players);
        Assert.True(WhistValidator.IsValid(candidate, out var reason),
            $"Wh({n}) validator failed: {reason}");
    }

    /// <summary>
    /// Reads the shipped base round by calling WhistMatchups.GetRoundMatchups for round 0
    /// and converting player IDs back to roles. Player IDs are 1..n; player 1 is inf.
    /// Finite role i in round 0 resolves to player i+2 (1-indexed; players[0]=inf=id 1,
    /// players[1+i].Id = i+2). So role(playerId) = playerId - 2 (or Inf if playerId == 1).
    /// </summary>
    private static BaseRoundCandidate ReconstructBaseRoundFromShipped(int n, List<Player> players)
    {
        var matches = WhistMatchups_GetRoundZero(players, n);
        var asTuples = matches.Select(m => (
            RoleFromPlayerId(m.Team1Player1Id),
            RoleFromPlayerId(m.Team1Player2Id),
            RoleFromPlayerId(m.Team2Player1Id),
            RoleFromPlayerId(m.Team2Player2Id)
        )).ToArray();
        return BaseRoundCandidate.FromTuples(n, asTuples);
    }

    private static int RoleFromPlayerId(int pid) =>
        pid == 1 ? BaseRoundCandidate.Inf : pid - 2;

    /// <summary>
    /// Calls into WhistMatchups via the public Whist path of ScheduleGenerator and extracts
    /// round 0 matches. Indirect because WhistMatchups is internal.
    /// </summary>
    private static List<Match> WhistMatchups_GetRoundZero(List<Player> players, int n)
    {
        var generator = new ScheduleGenerator();
        var schedule = generator.Generate(players, numberOfCourts: n / 4, numberOfRounds: 1);
        return schedule.Rounds[0].Matches;
    }
}
```

> NOTE on the `WhistMatchups_GetRoundZero` indirection: `WhistMatchups` is `internal`. We could add `InternalsVisibleTo`, but it's simpler to drive it through `ScheduleGenerator`'s public surface — that also catches integration regressions if the wiring breaks.

- [ ] **Step 9.2: Fill in the expected values from Task 7**

Update `ExpectedCoverageRounds()` with the four max-coverage values printed by the search runs.

- [ ] **Step 9.3: Run the new tests**

Run: `dotnet test PickleballScheduler.Tests --filter "FullyQualifiedName~WhistMatchupsTests"`
Expected: PASS (10 tests: 5 invariants + 5 coverage).

- [ ] **Step 9.4: Run the entire test suite**

Run: `dotnet test PickleballScheduler.sln`
Expected: All tests pass.

- [ ] **Step 9.5: Commit**

```
git add PickleballScheduler.Tests/Services/WhistMatchupsTests.cs
git commit -m "test(whist): add invariant and coverage regression tests for shipped base rounds"
```

---

## Task 10: Manual smoke test in the running app

This catches anything the unit tests miss (e.g. court-permutation interactions that are awkward to assert in tests).

- [ ] **Step 10.1: Run the app locally**

Run: `dotnet run --project PickleballScheduler`

- [ ] **Step 10.2: Generate a 16-player, 4-court, 15-round schedule**

In the browser, set up 16 players, 4 courts, 15 rounds. Click Generate.

- [ ] **Step 10.3: Visually verify**

Scan the printed schedule:
- No duplicate players within a single round.
- Court numbers vary per player across rounds (rough balance).
- The infinity player (P1 by default) shares a court with each of the other 15 players within the first ~5 rounds. (Manually check: list P1's court-mates round by round.)

If anything looks off, capture details and stop — investigate before merging.

- [ ] **Step 10.4: Repeat for n=8 and n=24**

Quick sanity passes for the smallest and largest canonical sizes.

- [ ] **Step 10.5: No commit unless a follow-up fix is needed**

If the smoke tests pass, the work is complete.

---

## Search performance contingency

If `SearchRunner.FindBest(n)` exceeds an acceptable runtime (say 10 minutes) for n=20 or n=24, the cleanest fallback is to add a difference-class pre-filter to `BaseRoundEnumerator.PartitionIntoMatches`:

- Maintain running totals of partner-difference classes and opponent-difference classes as matches are added.
- Prune any branch where a partner class would exceed 1 hit, or an opponent class would exceed 2 hits.
- This is the same constraint the `WhistValidator` checks at the end; pushing it into the enumerator turns full-enumeration into constraint-propagation search, dropping runtime by orders of magnitude.

Implementation sketch (only if needed):

```csharp
// In PartitionIntoMatches, accept partnerHits and opponentHits arrays as state.
// Before recursing on a candidate match, tentatively update the arrays and check bounds.
// If any class would exceed its limit, skip; otherwise recurse, then revert on backtrack.
```

If even with pruning the search is too slow, document the runtime in a comment in `SearchRunnerTests.cs` and proceed by best-effort: capture the best result found within a wall-clock budget. The regression tests in Task 9 still guard whatever max-coverage value is shipped.

---

## Self-review

Spec coverage:
- "Replace base rounds for n ∈ {8, 12, 16, 20, 24}" — Tasks 7, 8.
- "Minimize max first-coverage round, tiebreak sum" — `SearchRunner.FindBest` Task 6.
- "Validate via difference-class analysis" — Task 3.
- "Search code in test project, results hand-coded into production" — file structure / Tasks 6 vs 8.
- "Tests assert invariants and coverage on shipped rounds" — Task 9.
- "Existing tests continue to pass" — Tasks 8.3, 9.4.

No placeholders other than the explicit "fill from Task 7" markers, which have clear capture-and-paste instructions in Task 7.

Type consistency: `BaseRoundCandidate.Match` (Team1A/Team1B/Team2A/Team2B), `WhistValidator.IsValid(c, out reason)`, `CoverageCalculator.Result(MaxCoverageRound, SumCoverageRound, CoverageRoundByPlayer)`, `SearchRunner.Best(Candidate, MaxCoverageRound, SumCoverageRound)` — all referenced consistently.
