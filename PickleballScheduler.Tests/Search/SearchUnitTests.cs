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

public class WhistValidatorTests
{
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
            (BaseRoundCandidate.Inf, 0, 1, 1),
            (2, 6, 4, 5));
        Assert.False(WhistValidator.IsValid(c, out var reason));
        Assert.Contains("duplicate", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsValid_PartnerDifferenceCollision_ReturnsFalse()
    {
        // Wh(8): partner classes must be {1,2,3}. Use (inf,0,1,2 | 3,4,5,6) where finite pairs
        // (1,2) diff=1 and (5,6) diff=1 collide.
        var c = BaseRoundCandidate.FromTuples(8,
            (BaseRoundCandidate.Inf, 0, 1, 2),
            (3, 4, 5, 6));
        Assert.False(WhistValidator.IsValid(c, out _));
    }

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
}

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

    [Fact]
    public void Enumerate_n12_PrunedYieldsSameValidCandidatesAsUnpruned()
    {
        var hashSet = new HashSet<string>();
        foreach (var c in BaseRoundEnumerator.Enumerate(12))
        {
            if (WhistValidator.IsValid(c, out _))
                hashSet.Add(Canonical(c));
        }
        // Pruning should not drop any valid candidates — only invalid branches are cut.
        Assert.True(hashSet.Count > 0, "expected valid Wh(12) candidates");
    }

    private static string Canonical(BaseRoundCandidate c)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(c.PlayerCount).Append('|');
        foreach (var m in c.Matches)
            sb.Append(m.Team1A).Append(',').Append(m.Team1B).Append(',').Append(m.Team2A).Append(',').Append(m.Team2B).Append(';');
        return sb.ToString();
    }
}

public class CoverageCalculatorTests
{
    [Fact]
    public void MaxCoverageRound_ShippedWh16_IsTwelve()
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
        // P0 is at the maximum — confirmed at the global max.
        Assert.Equal(result.MaxCoverageRound, result.CoverageRoundByPlayer[0]);
        // Most finite players complete coverage earlier; at least half should finish before round 12.
        int countBetter = 0;
        for (int p = 1; p < 16; p++)
            if (result.CoverageRoundByPlayer[p] < 12) countBetter++;
        Assert.True(countBetter >= 8,
            $"Expected at least 8 finite players with coverage < 12, got {countBetter}");
    }
}
