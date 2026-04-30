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
