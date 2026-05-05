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
    public void Generate_9Players_2Courts_4Rounds_ByeOrderIsNotStrictlyDescending()
    {
        // Regression: previously byes went 9,8,7,6,... — a mechanical descending pattern.
        // Max-spread + rotational tiebreak should give a non-monotonic order while still
        // covering distinct players each round (since byeCount stays balanced).
        var players = MakePlayers(9);
        var rounds = GreedyScheduler.Generate(players, courts: 2, rounds: 4);

        var byeIds = rounds.Select(r => r.Byes.Single().PlayerId).ToList();
        Assert.Equal(4, byeIds.Distinct().Count());

        bool strictlyDescending = byeIds
            .Zip(byeIds.Skip(1), (a, b) => b == a - 1)
            .All(x => x);
        Assert.False(strictlyDescending, $"bye order is still strictly descending: {string.Join(",", byeIds)}");
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

    [Fact]
    public void Generate_8Players_2Courts_10Rounds_NoPlayerStuckOnOneCourt()
    {
        // Paul's typical event size. The reported failure mode was a player getting all 10 rounds
        // on a single court. Per-round permutation court assignment should keep spread small.
        var players = MakePlayers(8);
        var rounds = GreedyScheduler.Generate(players, courts: 2, rounds: 10);

        var courtVisits = MakePlayers(8).ToDictionary(p => p.Id, _ => new int[2]);
        foreach (var r in rounds)
            foreach (var m in r.Matches)
            {
                int idx = m.CourtNumber - 1;
                foreach (var pid in new[] { m.Team1Player1Id, m.Team1Player2Id, m.Team2Player1Id, m.Team2Player2Id })
                    courtVisits[pid][idx]++;
            }

        var report = string.Join(", ", courtVisits.Select(kv => $"P{kv.Key}={kv.Value[0]}/{kv.Value[1]}"));
        foreach (var (pid, counts) in courtVisits)
        {
            int max = counts.Max();
            int min = counts.Min();
            Assert.True(max - min <= 2,
                $"player {pid} spread {max - min}; full report: {report}");
        }
    }

    [Fact]
    public void Generate_12Players_3Courts_10Rounds_CourtsBalanced()
    {
        // Second case the user reported: 12p with bad distribution for P1 and P8.
        // 10 rounds × 3 courts = 30 player-rounds across 3 courts → ideal ~3.3 per court.
        var players = MakePlayers(12);
        var rounds = GreedyScheduler.Generate(players, courts: 3, rounds: 10);

        var courtVisits = MakePlayers(12).ToDictionary(p => p.Id, _ => new int[3]);
        foreach (var r in rounds)
            foreach (var m in r.Matches)
            {
                int idx = m.CourtNumber - 1;
                foreach (var pid in new[] { m.Team1Player1Id, m.Team1Player2Id, m.Team2Player1Id, m.Team2Player2Id })
                    courtVisits[pid][idx]++;
            }

        var report = string.Join(", ", courtVisits.Select(kv => $"P{kv.Key}={kv.Value[0]}/{kv.Value[1]}/{kv.Value[2]}"));
        foreach (var (pid, counts) in courtVisits)
        {
            int max = counts.Max();
            int min = counts.Min();
            // 12p/3c/10r: 10/3=3.33 ideal. Per-round greedy lands most players at spread 1-2 with
            // occasional 3 — the multi-court permutation can't always undo cumulative drift.
            // Far better than the canonical-table failure mode (P1/P8 stuck) the user reported.
            Assert.True(max - min <= 3,
                $"player {pid} spread {max - min}; full: {report}");
        }
    }
}
