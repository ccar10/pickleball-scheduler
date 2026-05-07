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
