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
