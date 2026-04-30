using PickleballScheduler.Models;
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
}
