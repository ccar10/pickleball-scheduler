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

    [Fact]
    public void Generate_4Players_ProducesRoundsWithUniquePartners()
    {
        var players = MakePlayers(4);
        var generator = new ScheduleGenerator();

        var result = generator.Generate(players, numberOfCourts: 1, numberOfRounds: 3);
        var rounds = result.Rounds;

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

        var result = generator.Generate(players, numberOfCourts: 1, numberOfRounds: 4);
        var rounds = result.Rounds;

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

        var result = generator.Generate(players, numberOfCourts: 2, numberOfRounds: 3);
        var rounds = result.Rounds;

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

        var result = generator.Generate(players, numberOfCourts: 2, numberOfRounds: 5);
        var rounds = result.Rounds;

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
    public void Generate_6Players_1Court_2ByesPerRound()
    {
        var players = MakePlayers(6);
        var generator = new ScheduleGenerator();

        var result = generator.Generate(players, numberOfCourts: 1, numberOfRounds: 4);
        var rounds = result.Rounds;

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

        var result = generator.Generate(players, numberOfCourts: 2, numberOfRounds: 3);
        var rounds = result.Rounds;

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

    [Fact]
    public void Generate_OpponentsVary_NoExcessiveRepeatOpponents()
    {
        var players = MakePlayers(8);
        var generator = new ScheduleGenerator();

        var result = generator.Generate(players, numberOfCourts: 2, numberOfRounds: 5);
        var rounds = result.Rounds;

        // Track how many times each pair of players oppose each other
        var opponentCounts = new Dictionary<string, int>();
        foreach (var round in rounds)
        {
            foreach (var match in round.Matches)
            {
                var team1 = new[] { match.Team1Player1Id, match.Team1Player2Id };
                var team2 = new[] { match.Team2Player1Id, match.Team2Player2Id };
                foreach (var p1 in team1)
                {
                    foreach (var p2 in team2)
                    {
                        var key = PairKey(p1, p2);
                        opponentCounts[key] = opponentCounts.GetValueOrDefault(key) + 1;
                    }
                }
            }
        }

        // With 8 players over 5 rounds, each player has 28 possible opponents pairs.
        // No opponent pair should appear excessively often.
        var maxOpponentRepeats = opponentCounts.Values.Max();
        Assert.True(maxOpponentRepeats <= 3,
            $"Max opponent repeats was {maxOpponentRepeats}, expected 3 or less for good variety");
    }

    [Fact]
    public void Generate_CourtAssignmentsVary()
    {
        var players = MakePlayers(8);
        var generator = new ScheduleGenerator();

        var result = generator.Generate(players, numberOfCourts: 2, numberOfRounds: 6);
        var rounds = result.Rounds;

        // Track court assignments per player
        var courtCounts = new Dictionary<int, Dictionary<int, int>>();
        foreach (var p in players)
        {
            courtCounts[p.Id] = new Dictionary<int, int>();
        }

        foreach (var round in rounds)
        {
            foreach (var match in round.Matches)
            {
                var playerIds = new[] {
                    match.Team1Player1Id, match.Team1Player2Id,
                    match.Team2Player1Id, match.Team2Player2Id
                };
                foreach (var pid in playerIds)
                {
                    var court = match.CourtNumber;
                    courtCounts[pid][court] = courtCounts[pid].GetValueOrDefault(court) + 1;
                }
            }
        }

        // With 2 courts over 6 rounds, each player plays all 6 rounds.
        // Ideal would be 3 on each court. Allow up to 2 difference.
        foreach (var pid in players.Select(p => p.Id))
        {
            var counts = courtCounts[pid];
            if (counts.Count > 1)
            {
                var max = counts.Values.Max();
                var min = counts.Values.Min();
                Assert.True(max - min <= 2,
                    $"Player {pid} has court imbalance: {string.Join(", ", counts.Select(kv => $"Court {kv.Key}: {kv.Value}"))}");
            }
        }
    }

    [Fact]
    public void Generate_4Players_1Court_3Rounds_AnyHr2Reported()
    {
        // 4 players / 1 court forces HR2 violations every round (you face the only 2 opponents).
        // Current algorithm doesn't avoid them. After Task 2, count should be > 0.
        var players = MakePlayers(4);
        var generator = new ScheduleGenerator();

        var result = generator.Generate(players, numberOfCourts: 1, numberOfRounds: 3);

        Assert.True(result.Hr2Violations > 0,
            $"Expected HR2 violations on 4p/1c/3r config, got {result.Hr2Violations}");
    }

    [Fact]
    public void Generate_8Players_2Courts_10Rounds_NoConsecutiveOpponents()
    {
        // Paul's representative case. After the joint search lands, HR2 must be 0.
        var players = MakePlayers(8);
        var generator = new ScheduleGenerator();

        var result = generator.Generate(players, numberOfCourts: 2, numberOfRounds: 10);

        Assert.Equal(0, result.Hr1Violations);
        Assert.Equal(0, result.Hr2Violations);
    }

    [Fact]
    public void TrySuggestZeroViolationConfig_8Players_2Courts_3Rounds_ReturnsNull()
    {
        // 8/2/3 already produces zero violations — no suggestion needed.
        var players = MakePlayers(8);
        var suggestion = ScheduleGenerator.TrySuggestZeroViolationConfig(
            players, courts: 2, rounds: 3);
        Assert.Null(suggestion);
    }

    [Fact]
    public void TrySuggestZeroViolationConfig_4Players_1Court_5Rounds_SuggestsBetterConfig()
    {
        // 4/1/5 forces HR2 every round. A near-neighbor config (e.g., 6/1/5) can clear it.
        var players = MakePlayers(4);
        var suggestion = ScheduleGenerator.TrySuggestZeroViolationConfig(
            players, courts: 1, rounds: 5);
        Assert.NotNull(suggestion);
        Assert.Contains("players", suggestion!);
    }

    [Fact]
    public void Generate_8Players_2Courts_7Rounds_PerfectWhistCycle()
    {
        var players = MakePlayers(8);
        var generator = new ScheduleGenerator();

        var result = generator.Generate(players, numberOfCourts: 2, numberOfRounds: 7);

        Assert.Equal(0, result.Hr1Violations);
        Assert.Equal(0, result.Hr2Violations);

        var partnerCounts = new Dictionary<string, int>();
        var opponentCounts = new Dictionary<string, int>();
        foreach (var round in result.Rounds)
            foreach (var m in round.Matches)
            {
                IncrementPair(partnerCounts, m.Team1Player1Id, m.Team1Player2Id);
                IncrementPair(partnerCounts, m.Team2Player1Id, m.Team2Player2Id);
                foreach (var p1 in new[] { m.Team1Player1Id, m.Team1Player2Id })
                    foreach (var p2 in new[] { m.Team2Player1Id, m.Team2Player2Id })
                        IncrementPair(opponentCounts, p1, p2);
            }

        Assert.Equal(28, partnerCounts.Count);
        Assert.All(partnerCounts.Values, v => Assert.Equal(1, v));
        Assert.Equal(28, opponentCounts.Count);
        Assert.All(opponentCounts.Values, v => Assert.Equal(2, v));
    }

    [Fact]
    public void Generate_10Players_2Courts_9Rounds_FallsBackToJointSearch()
    {
        // 10 is not a Whist size; the joint search should handle it without exceptions.
        var players = MakePlayers(10);
        var generator = new ScheduleGenerator();

        var result = generator.Generate(players, numberOfCourts: 2, numberOfRounds: 9);

        Assert.Equal(9, result.Rounds.Count);
        foreach (var round in result.Rounds)
        {
            var seen = new HashSet<int>();
            foreach (var m in round.Matches)
                foreach (var pid in new[] { m.Team1Player1Id, m.Team1Player2Id, m.Team2Player1Id, m.Team2Player2Id })
                    Assert.True(seen.Add(pid), $"Player {pid} double-booked in round {round.RoundNumber}");
        }
    }

    private static void IncrementPair(Dictionary<string, int> counts, int a, int b)
    {
        var key = a < b ? $"{a}-{b}" : $"{b}-{a}";
        counts[key] = counts.GetValueOrDefault(key) + 1;
    }

    private static string PairKey(int a, int b)
        => a < b ? $"{a}-{b}" : $"{b}-{a}";
}
