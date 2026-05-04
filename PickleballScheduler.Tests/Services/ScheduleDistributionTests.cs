using PickleballScheduler.Models;
using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

public class ScheduleDistributionTests
{
    public static TheoryData<int, int, int, int> Configs()
    {
        var data = new TheoryData<int, int, int, int>();
        var rng = new Random(20260430);
        for (int i = 0; i < 50; i++)
        {
            int players = rng.Next(4, 25);
            int courts = rng.Next(1, 7);
            int rounds = rng.Next(3, 16);
            data.Add(players, courts, rounds, i);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Configs))]
    public void Generate_NoStructuralViolations(int playerCount, int courtCount, int rounds, int seed)
    {
        var players = Enumerable.Range(1, playerCount)
            .Select(i => new Player { Id = i, Name = $"P{i}" })
            .ToList();
        var generator = new ScheduleGenerator();

        ScheduleResult result;
        try
        {
            result = generator.Generate(players, courtCount, rounds);
        }
        catch (Exception ex)
        {
            Assert.Fail($"[{playerCount}p/{courtCount}c/{rounds}r seed={seed}] threw: {ex.Message}");
            return;
        }

        Assert.Equal(rounds, result.Rounds.Count);

        foreach (var round in result.Rounds)
        {
            var seen = new HashSet<int>();
            foreach (var match in round.Matches)
            {
                Assert.InRange(match.CourtNumber, 1, courtCount);
                foreach (var pid in new[] { match.Team1Player1Id, match.Team1Player2Id, match.Team2Player1Id, match.Team2Player2Id })
                    Assert.True(seen.Add(pid),
                        $"[{playerCount}p/{courtCount}c/{rounds}r seed={seed}] player {pid} double-booked round {round.RoundNumber}");
            }
            foreach (var b in round.Byes)
                Assert.True(seen.Add(b.PlayerId),
                    $"[{playerCount}p/{courtCount}c/{rounds}r seed={seed}] bye player {b.PlayerId} also playing round {round.RoundNumber}");
        }
    }
}
