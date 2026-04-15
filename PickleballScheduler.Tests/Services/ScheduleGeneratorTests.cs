using PickleballScheduler.Models;
using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

public class ScheduleGeneratorTests
{
    private static List<Player> MakePlayers(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => new Player { Id = i, Name = $"Player {i}" })
            .ToList();
    }

    private static List<Player> MakeRatedPlayers(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => new Player { Id = i, Name = $"Player {i}", DuprRating = 2.0m + (i * 0.5m) })
            .ToList();
    }

    [Fact]
    public void Generate_4Players_ProducesRoundsWithUniquePartners()
    {
        var players = MakePlayers(4);
        var generator = new ScheduleGenerator();

        var rounds = generator.Generate(players, numberOfCourts: 1, numberOfRounds: 3, useSkillBalancing: false);

        Assert.Equal(3, rounds.Count);

        var partnerships = new HashSet<string>();
        foreach (var round in rounds)
        {
            Assert.Single(round.Matches);
            Assert.Empty(round.Byes);

            var match = round.Matches[0];
            var pair1 = PairKey(match.Team1Player1Id, match.Team1Player2Id);
            var pair2 = PairKey(match.Team2Player1Id, match.Team2Player2Id);

            Assert.DoesNotContain(pair1, partnerships);
            Assert.DoesNotContain(pair2, partnerships);
            partnerships.Add(pair1);
            partnerships.Add(pair2);
        }
    }

    [Fact]
    public void Generate_5Players_AssignsByesEvenly()
    {
        var players = MakePlayers(5);
        var generator = new ScheduleGenerator();

        var rounds = generator.Generate(players, numberOfCourts: 1, numberOfRounds: 4, useSkillBalancing: false);

        Assert.Equal(4, rounds.Count);

        var byeCounts = new Dictionary<int, int>();
        foreach (var round in rounds)
        {
            Assert.Single(round.Matches);
            Assert.Single(round.Byes);
            var byePlayerId = round.Byes[0].PlayerId;
            byeCounts[byePlayerId] = byeCounts.GetValueOrDefault(byePlayerId) + 1;
        }

        var maxByes = byeCounts.Values.Max();
        var minByes = byeCounts.Values.Min();
        Assert.True(maxByes - minByes <= 1, "Byes should be distributed evenly");
    }

    [Fact]
    public void Generate_8Players_2Courts_FillsBothCourts()
    {
        var players = MakePlayers(8);
        var generator = new ScheduleGenerator();

        var rounds = generator.Generate(players, numberOfCourts: 2, numberOfRounds: 3, useSkillBalancing: false);

        Assert.Equal(3, rounds.Count);
        foreach (var round in rounds)
        {
            Assert.Equal(2, round.Matches.Count);
            Assert.Empty(round.Byes);
        }
    }

    [Fact]
    public void Generate_NoRepeatedPartners()
    {
        var players = MakePlayers(8);
        var generator = new ScheduleGenerator();

        var rounds = generator.Generate(players, numberOfCourts: 2, numberOfRounds: 5, useSkillBalancing: false);

        var partnerships = new HashSet<string>();
        foreach (var round in rounds)
        {
            foreach (var match in round.Matches)
            {
                var pair1 = PairKey(match.Team1Player1Id, match.Team1Player2Id);
                var pair2 = PairKey(match.Team2Player1Id, match.Team2Player2Id);

                Assert.DoesNotContain(pair1, partnerships);
                Assert.DoesNotContain(pair2, partnerships);
                partnerships.Add(pair1);
                partnerships.Add(pair2);
            }
        }
    }

    [Fact]
    public void Generate_SkillBalancing_PairsHighWithLow()
    {
        var players = MakeRatedPlayers(4);
        var generator = new ScheduleGenerator();

        var rounds = generator.Generate(players, numberOfCourts: 1, numberOfRounds: 1, useSkillBalancing: true);

        var match = rounds[0].Matches[0];
        var team1Ratings = new[] {
            players.First(p => p.Id == match.Team1Player1Id).DuprRating!.Value,
            players.First(p => p.Id == match.Team1Player2Id).DuprRating!.Value
        };
        var team2Ratings = new[] {
            players.First(p => p.Id == match.Team2Player1Id).DuprRating!.Value,
            players.First(p => p.Id == match.Team2Player2Id).DuprRating!.Value
        };

        var team1Avg = team1Ratings.Average();
        var team2Avg = team2Ratings.Average();

        Assert.True(Math.Abs(team1Avg - team2Avg) <= 1.0m,
            $"Team averages should be balanced: {team1Avg} vs {team2Avg}");
    }

    [Fact]
    public void Generate_6Players_1Court_2ByesPerRound()
    {
        var players = MakePlayers(6);
        var generator = new ScheduleGenerator();

        var rounds = generator.Generate(players, numberOfCourts: 1, numberOfRounds: 4, useSkillBalancing: false);

        foreach (var round in rounds)
        {
            Assert.Single(round.Matches);
            Assert.Equal(2, round.Byes.Count);
        }
    }

    [Fact]
    public void Generate_AllPlayersAppearInEveryRound()
    {
        var players = MakePlayers(8);
        var generator = new ScheduleGenerator();

        var rounds = generator.Generate(players, numberOfCourts: 2, numberOfRounds: 3, useSkillBalancing: false);

        foreach (var round in rounds)
        {
            var playerIds = new HashSet<int>();
            foreach (var match in round.Matches)
            {
                playerIds.Add(match.Team1Player1Id);
                playerIds.Add(match.Team1Player2Id);
                playerIds.Add(match.Team2Player1Id);
                playerIds.Add(match.Team2Player2Id);
            }
            foreach (var bye in round.Byes)
            {
                playerIds.Add(bye.PlayerId);
            }

            Assert.Equal(players.Count, playerIds.Count);
        }
    }

    private static string PairKey(int a, int b)
        => a < b ? $"{a}-{b}" : $"{b}-{a}";
}
