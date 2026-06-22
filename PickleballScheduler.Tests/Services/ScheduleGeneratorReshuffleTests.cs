using PickleballScheduler.Models;
using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

public class ScheduleGeneratorReshuffleTests
{
    private static List<Player> MakePlayers(int count) =>
        Enumerable.Range(1, count).Select(i => new Player { Id = i * 10, Name = $"P{i}" }).ToList();

    private static string Signature(ScheduleResult result) => string.Join("|",
        result.Rounds.OrderBy(r => r.RoundNumber)
            .SelectMany(r => r.Matches.OrderBy(m => m.CourtNumber)
                .Select(m => $"{m.Team1Player1Id},{m.Team1Player2Id},{m.Team2Player1Id},{m.Team2Player2Id}")));

    [Fact]
    public void GenerateShuffled_DifferentSeeds_ProduceDifferentMatchups_For24p4c()
    {
        // 24 players / 4 courts is a greedy (non-Whist) config: deterministic by id,
        // so plain Generate yields the same matchups regardless of order. Reshuffle
        // must actually vary the games.
        var players = MakePlayers(24);

        var a = new ScheduleGenerator().GenerateShuffled(players, numberOfCourts: 4, numberOfRounds: 6, seed: 1);
        var b = new ScheduleGenerator().GenerateShuffled(players, numberOfCourts: 4, numberOfRounds: 6, seed: 2);

        Assert.NotEqual(Signature(a), Signature(b));
    }

    [Fact]
    public void GenerateShuffled_ProducesValidSchedule_AllRealPlayersEachRound()
    {
        var players = MakePlayers(24);
        var realIds = players.Select(p => p.Id).ToHashSet();

        var result = new ScheduleGenerator().GenerateShuffled(players, numberOfCourts: 4, numberOfRounds: 6, seed: 7);

        Assert.Equal(6, result.Rounds.Count);
        foreach (var round in result.Rounds)
        {
            Assert.Equal(4, round.Matches.Count);
            var ids = round.Matches
                .SelectMany(m => new[] { m.Team1Player1Id, m.Team1Player2Id, m.Team2Player1Id, m.Team2Player2Id })
                .Concat(round.Byes.Select(b => b.PlayerId))
                .ToList();

            Assert.Equal(24, ids.Count);              // 16 playing + 8 byes
            Assert.Equal(24, ids.Distinct().Count()); // no player appears twice
            Assert.True(ids.All(realIds.Contains));   // mapped back to real ids
        }
    }

    [Fact]
    public void GenerateShuffled_SameSeed_IsDeterministic()
    {
        var players = MakePlayers(24);

        var a = new ScheduleGenerator().GenerateShuffled(players, 4, 6, seed: 42);
        var b = new ScheduleGenerator().GenerateShuffled(players, 4, 6, seed: 42);

        Assert.Equal(Signature(a), Signature(b));
    }
}
