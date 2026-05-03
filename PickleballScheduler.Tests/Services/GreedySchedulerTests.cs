using PickleballScheduler.Models;
using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

public class GreedySchedulerTests
{
    private static List<Player> MakePlayers(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new Player { Id = i, Name = $"P{i}" })
            .ToList();

    [Fact]
    public void Generate_8Players_2Courts_4Rounds_AllPlayersEachRound()
    {
        var players = MakePlayers(8);
        var rounds = GreedyScheduler.Generate(players, courts: 2, rounds: 4);

        Assert.Equal(4, rounds.Count);
        foreach (var r in rounds)
        {
            Assert.Equal(2, r.Matches.Count);
            Assert.Empty(r.Byes);
            var ids = r.Matches
                .SelectMany(m => new[] { m.Team1Player1Id, m.Team1Player2Id, m.Team2Player1Id, m.Team2Player2Id })
                .ToList();
            Assert.Equal(8, ids.Count);
            Assert.Equal(8, ids.Distinct().Count());
        }
    }

    [Fact]
    public void Generate_7Players_1Court_7Rounds_RotatesByesEvenly()
    {
        var players = MakePlayers(7);
        var rounds = GreedyScheduler.Generate(players, courts: 1, rounds: 7);

        Assert.Equal(7, rounds.Count);
        var byeCount = MakePlayers(7).ToDictionary(p => p.Id, _ => 0);
        foreach (var r in rounds)
        {
            Assert.Single(r.Matches);
            Assert.Equal(3, r.Byes.Count);
            foreach (var b in r.Byes) byeCount[b.PlayerId]++;
        }
        var max = byeCount.Values.Max();
        var min = byeCount.Values.Min();
        Assert.True(max - min <= 1, $"bye spread {max - min}");
    }

    [Fact]
    public void Generate_14Players_3Courts_8Rounds_NoDoubleBooking()
    {
        var players = MakePlayers(14);
        var rounds = GreedyScheduler.Generate(players, courts: 3, rounds: 8);

        Assert.Equal(8, rounds.Count);
        foreach (var r in rounds)
        {
            Assert.Equal(3, r.Matches.Count);
            Assert.Equal(2, r.Byes.Count);
            var seen = new HashSet<int>();
            foreach (var m in r.Matches)
                foreach (var pid in new[] { m.Team1Player1Id, m.Team1Player2Id, m.Team2Player1Id, m.Team2Player2Id })
                    Assert.True(seen.Add(pid), $"player {pid} double-booked");
            foreach (var b in r.Byes)
                Assert.True(seen.Add(b.PlayerId), $"bye player {b.PlayerId} also playing");
            Assert.Equal(14, seen.Count);
        }
    }

    [Fact]
    public void Generate_DeterministicGivenIdenticalInput()
    {
        var p1 = MakePlayers(12);
        var p2 = MakePlayers(12);
        var run1 = GreedyScheduler.Generate(p1, courts: 3, rounds: 5);
        var run2 = GreedyScheduler.Generate(p2, courts: 3, rounds: 5);

        Assert.Equal(run1.Count, run2.Count);
        for (int i = 0; i < run1.Count; i++)
        {
            Assert.Equal(run1[i].Matches.Count, run2[i].Matches.Count);
            for (int j = 0; j < run1[i].Matches.Count; j++)
            {
                Assert.Equal(run1[i].Matches[j].Team1Player1Id, run2[i].Matches[j].Team1Player1Id);
                Assert.Equal(run1[i].Matches[j].Team2Player1Id, run2[i].Matches[j].Team2Player1Id);
                Assert.Equal(run1[i].Matches[j].CourtNumber, run2[i].Matches[j].CourtNumber);
            }
        }
    }
}
