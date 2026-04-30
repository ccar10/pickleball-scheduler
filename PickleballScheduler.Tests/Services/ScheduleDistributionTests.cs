using PickleballScheduler.Models;
using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

public class ScheduleDistributionTests
{
    public static TheoryData<int, int, int, int> Configs()
    {
        var data = new TheoryData<int, int, int, int>();
        var rng = new Random(20260430);
        for (int i = 0; i < 100; i++)
        {
            int players = rng.Next(4, 25);   // 4..24
            int courts = rng.Next(1, 7);     // 1..6
            int rounds = rng.Next(3, 16);    // 3..15
            data.Add(players, courts, rounds, i);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Configs))]
    public void Generate_DistributionMetrics(int playerCount, int courtCount, int rounds, int seed)
    {
        var players = Enumerable.Range(1, playerCount)
            .Select(i => new Player { Id = i, Name = $"Player {i}" })
            .ToList();
        var generator = new ScheduleGenerator();

        var result = generator.Generate(players, courtCount, rounds);

        // HR1 violations
        var maxHr1 = ScheduleFeasibility.Hr1RepeatTolerance(playerCount, courtCount, rounds);
        Assert.True(result.Hr1Violations <= maxHr1,
            $"[{playerCount}p/{courtCount}c/{rounds}r seed={seed}] HR1={result.Hr1Violations} > max={maxHr1}");

        // HR2 violations
        if (ScheduleFeasibility.Hr2Feasible(playerCount, courtCount))
        {
            Assert.True(result.Hr2Violations == 0,
                $"[{playerCount}p/{courtCount}c/{rounds}r seed={seed}] HR2={result.Hr2Violations}, expected 0");
        }

        // Partner spread
        var partnerCounts = ComputePartnerCounts(result.Rounds);
        if (partnerCounts.Count > 0)
        {
            var spread = partnerCounts.Values.Max() - partnerCounts.Values.Min();
            Assert.True(spread <= 1,
                $"[{playerCount}p/{courtCount}c/{rounds}r seed={seed}] partner spread={spread}");
        }

        // Opponent spread
        var opponentCounts = ComputeOpponentCounts(result.Rounds);
        if (opponentCounts.Count > 0)
        {
            var spread = opponentCounts.Values.Max() - opponentCounts.Values.Min();
            Assert.True(spread <= 2,
                $"[{playerCount}p/{courtCount}c/{rounds}r seed={seed}] opponent spread={spread}");
        }

        // Bye fairness
        var byeCounts = ComputeByeCounts(players, result.Rounds);
        if (byeCounts.Values.Any())
        {
            var spread = byeCounts.Values.Max() - byeCounts.Values.Min();
            Assert.True(spread <= 1,
                $"[{playerCount}p/{courtCount}c/{rounds}r seed={seed}] bye spread={spread}");
        }

        // Per-player court spread
        foreach (var p in players)
        {
            var counts = ComputeCourtCountsForPlayer(p.Id, result.Rounds);
            if (counts.Count > 1)
            {
                var spread = counts.Values.Max() - counts.Values.Min();
                Assert.True(spread <= 2,
                    $"[{playerCount}p/{courtCount}c/{rounds}r seed={seed}] court spread for player {p.Id}={spread}");
            }
        }

        // Coverage: every pair partners when configuration permits.
        if (rounds >= playerCount - 1 && playerCount % 2 == 0 && playerCount / 2 <= courtCount)
        {
            var allPairs = new HashSet<string>();
            for (int a = 1; a <= playerCount; a++)
                for (int b = a + 1; b <= playerCount; b++)
                    allPairs.Add($"{a}-{b}");
            var seenPairs = partnerCounts.Keys.ToHashSet();
            Assert.True(allPairs.IsSubsetOf(seenPairs),
                $"[{playerCount}p/{courtCount}c/{rounds}r seed={seed}] missing pairs: {string.Join(", ", allPairs.Except(seenPairs))}");
        }
    }

    private static Dictionary<string, int> ComputePartnerCounts(List<Round> rounds)
    {
        var counts = new Dictionary<string, int>();
        foreach (var round in rounds)
            foreach (var match in round.Matches)
            {
                Increment(counts, match.Team1Player1Id, match.Team1Player2Id);
                Increment(counts, match.Team2Player1Id, match.Team2Player2Id);
            }
        return counts;
    }

    private static Dictionary<string, int> ComputeOpponentCounts(List<Round> rounds)
    {
        var counts = new Dictionary<string, int>();
        foreach (var round in rounds)
            foreach (var match in round.Matches)
            {
                var team1 = new[] { match.Team1Player1Id, match.Team1Player2Id };
                var team2 = new[] { match.Team2Player1Id, match.Team2Player2Id };
                foreach (var p1 in team1)
                    foreach (var p2 in team2)
                        Increment(counts, p1, p2);
            }
        return counts;
    }

    private static Dictionary<int, int> ComputeByeCounts(List<Player> players, List<Round> rounds)
    {
        var counts = players.ToDictionary(p => p.Id, _ => 0);
        foreach (var round in rounds)
            foreach (var bye in round.Byes)
                if (counts.ContainsKey(bye.PlayerId)) counts[bye.PlayerId]++;
        return counts;
    }

    private static Dictionary<int, int> ComputeCourtCountsForPlayer(int playerId, List<Round> rounds)
    {
        var counts = new Dictionary<int, int>();
        foreach (var round in rounds)
            foreach (var match in round.Matches)
            {
                var ids = new[] { match.Team1Player1Id, match.Team1Player2Id, match.Team2Player1Id, match.Team2Player2Id };
                if (ids.Contains(playerId))
                    counts[match.CourtNumber] = counts.GetValueOrDefault(match.CourtNumber) + 1;
            }
        return counts;
    }

    private static void Increment(Dictionary<string, int> counts, int a, int b)
    {
        var key = a < b ? $"{a}-{b}" : $"{b}-{a}";
        counts[key] = counts.GetValueOrDefault(key) + 1;
    }

    [Fact]
    public void StressTest_NoStructuralViolations()
    {
        // Sample size kept moderate to fit in CI test budget; joint search can be slow on
        // 16-active configs (Task 15 will add a beam-search fallback). The intent is to
        // surface exceptions and double-booking bugs, not to verify fairness (covered by
        // Generate_DistributionMetrics).
        const int Samples = 50;

        var rng = new Random(20260430);
        for (int i = 0; i < Samples; i++)
        {
            int playerCount = rng.Next(4, 25);
            int courts = rng.Next(1, 7);
            int rounds = rng.Next(3, 16);

            var players = Enumerable.Range(1, playerCount)
                .Select(id => new Player { Id = id, Name = $"P{id}" })
                .ToList();
            var generator = new ScheduleGenerator();

            ScheduleResult result;
            try
            {
                result = generator.Generate(players, courts, rounds);
            }
            catch (Exception ex)
            {
                Assert.Fail($"Threw on config p={playerCount} c={courts} r={rounds} (i={i}): {ex.Message}");
                return;
            }

            foreach (var round in result.Rounds)
            {
                var seen = new HashSet<int>();
                foreach (var match in round.Matches)
                {
                    Assert.InRange(match.CourtNumber, 1, courts);
                    foreach (var pid in new[] { match.Team1Player1Id, match.Team1Player2Id, match.Team2Player1Id, match.Team2Player2Id })
                    {
                        Assert.True(seen.Add(pid),
                            $"Player {pid} double-booked in round {round.RoundNumber} (config p={playerCount} c={courts} r={rounds} i={i})");
                    }
                }
            }
        }
    }
}
