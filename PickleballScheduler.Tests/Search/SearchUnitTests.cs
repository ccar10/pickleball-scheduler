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

