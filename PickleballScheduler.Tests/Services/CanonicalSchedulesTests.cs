using PickleballScheduler.Models;
using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

public class CanonicalSchedulesTests
{
    // SkipUntilTablesPopulated is removed in Task 10 once tables are in place.
    private const string SkipUntilTablesPopulated = "Tables populated in Task 10";

    [Theory(Skip = SkipUntilTablesPopulated)]
    [InlineData(8, 2)]
    [InlineData(12, 3)]
    [InlineData(16, 4)]
    [InlineData(20, 5)]
    [InlineData(24, 6)]
    public void IsCanonical_TruePairsAreRecognized(int n, int c)
    {
        Assert.True(CanonicalSchedules.IsCanonical(n, c),
            $"({n}, {c}) should be canonical once tables are populated");
    }

    [Theory(Skip = SkipUntilTablesPopulated)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    public void TableHasCorrectShape(int n)
    {
        var players = MakePlayers(n);
        var courts = n / 4;

        for (int r = 0; r < CanonicalSchedules.RoundCount; r++)
        {
            var matches = CanonicalSchedules.GetRound(n, r, players);
            Assert.Equal(courts, matches.Count);

            var seen = new HashSet<int>();
            var seenCourts = new HashSet<int>();
            foreach (var m in matches)
            {
                foreach (var id in new[] { m.Team1Player1Id, m.Team1Player2Id, m.Team2Player1Id, m.Team2Player2Id })
                    Assert.True(seen.Add(id), $"round {r}: player {id} appears twice");
                Assert.InRange(m.CourtNumber, 1, courts);
                Assert.True(seenCourts.Add(m.CourtNumber), $"round {r}: court {m.CourtNumber} reused");
            }
            Assert.Equal(n, seen.Count);
        }
    }

    [Theory(Skip = SkipUntilTablesPopulated)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    public void NoConsecutivePartnerRepeats(int n)
    {
        var players = MakePlayers(n);
        var prev = new HashSet<string>();
        for (int r = 0; r < CanonicalSchedules.RoundCount; r++)
        {
            var current = new HashSet<string>();
            var matches = CanonicalSchedules.GetRound(n, r, players);
            foreach (var m in matches)
            {
                current.Add(PairKey(m.Team1Player1Id, m.Team1Player2Id));
                current.Add(PairKey(m.Team2Player1Id, m.Team2Player2Id));
            }
            if (r > 0)
            {
                var overlap = prev.Intersect(current).ToList();
                Assert.Empty(overlap);
            }
            prev = current;
        }
    }

    [Theory(Skip = SkipUntilTablesPopulated)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    public void PartnerCountSpreadIsAtMostOne(int n)
    {
        var players = MakePlayers(n);
        var partnerCounts = new Dictionary<string, int>();
        for (int r = 0; r < CanonicalSchedules.RoundCount; r++)
        {
            var matches = CanonicalSchedules.GetRound(n, r, players);
            foreach (var m in matches)
            {
                Increment(partnerCounts, m.Team1Player1Id, m.Team1Player2Id);
                Increment(partnerCounts, m.Team2Player1Id, m.Team2Player2Id);
            }
        }
        // Every pair appears at least once if 30 rounds covers them; spread = max - min over actual counts.
        var max = partnerCounts.Values.Max();
        var min = partnerCounts.Values.Min();
        Assert.True(max - min <= 1, $"partner spread {max - min}: max={max}, min={min}");
    }

    [Theory(Skip = SkipUntilTablesPopulated)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    public void CourtVisitSpreadIsAtMostOne(int n)
    {
        var players = MakePlayers(n);
        var courts = n / 4;
        var courtVisits = players.ToDictionary(p => p.Id, _ => new int[courts]);

        for (int r = 0; r < CanonicalSchedules.RoundCount; r++)
        {
            var matches = CanonicalSchedules.GetRound(n, r, players);
            foreach (var m in matches)
            {
                int idx = m.CourtNumber - 1;
                foreach (var pid in new[] { m.Team1Player1Id, m.Team1Player2Id, m.Team2Player1Id, m.Team2Player2Id })
                    courtVisits[pid][idx]++;
            }
        }
        foreach (var (pid, counts) in courtVisits)
        {
            int max = counts.Max();
            int min = counts.Min();
            Assert.True(max - min <= 1,
                $"player {pid} court visits {string.Join(",", counts)} spread {max - min}");
        }
    }

    private static List<Player> MakePlayers(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new Player { Id = i, Name = $"P{i}" })
            .ToList();

    private static string PairKey(int a, int b) => a < b ? $"{a}-{b}" : $"{b}-{a}";

    private static void Increment(Dictionary<string, int> counts, int a, int b)
    {
        var key = PairKey(a, b);
        counts[key] = counts.GetValueOrDefault(key) + 1;
    }
}
