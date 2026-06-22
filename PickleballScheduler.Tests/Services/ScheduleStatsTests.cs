using PickleballScheduler.Models;
using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

public class ScheduleStatsTests
{
    private static Match Court(int p1, int p2, int p3, int p4, int court = 1) =>
        new() { CourtNumber = court, Team1Player1Id = p1, Team1Player2Id = p2, Team2Player1Id = p3, Team2Player2Id = p4 };

    [Fact]
    public void PartnerRepeatPairs_FindsPairsPartneringMoreThanOnce()
    {
        var rounds = new List<Round>
        {
            new() { RoundNumber = 1, Matches = { Court(1, 2, 3, 4) } },
            new() { RoundNumber = 2, Matches = { Court(1, 2, 3, 4) } }, // 1&2 partner again, 3&4 partner again
        };

        var repeats = ScheduleStats.PartnerRepeatPairs(rounds);

        Assert.Contains((1, 2), repeats);
        Assert.Contains((3, 4), repeats);
        Assert.Equal(2, repeats.Count);
    }

    [Fact]
    public void PartnerRepeatPairs_NoRepeats_ReturnsEmpty()
    {
        var rounds = new List<Round>
        {
            new() { RoundNumber = 1, Matches = { Court(1, 2, 3, 4) } },
            new() { RoundNumber = 2, Matches = { Court(1, 3, 2, 4) } }, // all-new partners
        };

        Assert.Empty(ScheduleStats.PartnerRepeatPairs(rounds));
    }
}
