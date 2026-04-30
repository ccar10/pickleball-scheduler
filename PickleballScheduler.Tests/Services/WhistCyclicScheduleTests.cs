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
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
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

    /// <summary>
    /// One-shot brute-force generator for Whist Cyclic base rounds. Searches for a
    /// valid arrangement of n/4 matches over players {inf, 0..n-2} satisfying the
    /// partner-once / oppose-twice invariants under cyclic rotation mod (n-1).
    /// Skipped by default — re-enable, run a single size, copy the printed
    /// transcript into <c>WhistCyclicSchedule.BaseRounds</c>.
    /// </summary>
    [Theory(Skip = "one-shot generator; remove Skip to regenerate a base round")]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    public void GenerateBaseRound(int n)
    {
        var result = BruteForceBaseRound(n);
        Assert.NotNull(result);
        var output = string.Join("\n", result!.Select(m =>
            $"new BaseMatch(\"{m[0]}\", \"{m[1]}\", \"{m[2]}\", \"{m[3]}\"),"));
        Assert.Fail("Generated base round for Wh(" + n + "):\n" + output);
    }

    /// <summary>
    /// Searches for a base round for Wh(n). Returns a list of matches, each as a
    /// 4-string array [A,B,C,D] meaning team (A,B) vs team (C,D). The match
    /// containing "inf" is always first; integer roles are in {0..n-2}.
    /// </summary>
    internal static List<string[]>? BruteForceBaseRound(int n)
    {
        // Strategy: pick the inf match and the remaining (k-1) integer matches such
        // that:
        //   (1) Every integer in 0..n-2 appears exactly once (4k - 1 = n - 1 slots).
        //   (2) Partner differences (one per partner pair, taken mod (n-1) as
        //       d -> min(d, n-1-d)) cover each value 1..(n-2)/2 exactly once.
        //   (3) Opponent differences (one per opponent pair) cover each value
        //       1..(n-2)/2 exactly twice.
        int k = n / 4;
        int m = n - 1;
        int half = m / 2; // (m-1)/2 since m is odd; covers 1..half

        // The inf match has form (inf, a) vs (b, c). 'a' partners inf and gets
        // covered by inf rotation; the integer partner pair in this match is (b,c).
        // Opponents in this match are (inf,b), (inf,c), (a,b), (a,c) -> integer
        // diffs |a-b|, |a-c|.

        // Enumerate the inf match systematically: choose unordered triple {a,b,c}.
        // Then for each, search the remaining 4(k-1) integers for a valid partition.
        for (int infA = 0; infA < m; infA++)
        {
            for (int infB = 0; infB < m; infB++)
            {
                if (infB == infA) continue;
                for (int infC = infB + 1; infC < m; infC++)
                {
                    if (infC == infA) continue;
                    // Avoid permutation duplicates: require infB < infC (already)
                    // and we'll try both orderings of (b,c) implicitly later via
                    // partner pair (b,c).
                    var infPartnerDiff = NormDiff(infB, infC, m);
                    var infOppDiff1 = NormDiff(infA, infB, m);
                    var infOppDiff2 = NormDiff(infA, infC, m);

                    var remaining = new List<int>();
                    for (int x = 0; x < m; x++)
                        if (x != infA && x != infB && x != infC) remaining.Add(x);

                    var partnerSeen = new int[half + 1];
                    var oppSeen = new int[half + 1];
                    partnerSeen[infPartnerDiff]++;
                    oppSeen[infOppDiff1]++;
                    oppSeen[infOppDiff2]++;
                    if (partnerSeen[infPartnerDiff] > 1) continue;

                    var matches = new List<int[]>();
                    if (SearchMatches(remaining, k - 1, partnerSeen, oppSeen, half, m, matches))
                    {
                        var result = new List<string[]>
                        {
                            new[] { "inf", infA.ToString(), infB.ToString(), infC.ToString() }
                        };
                        foreach (var match in matches)
                            result.Add(new[]
                            {
                                match[0].ToString(), match[1].ToString(),
                                match[2].ToString(), match[3].ToString(),
                            });
                        return result;
                    }
                }
            }
        }
        return null;
    }

    private static bool SearchMatches(List<int> pool, int matchesLeft,
        int[] partnerSeen, int[] oppSeen, int half, int m,
        List<int[]> output)
    {
        if (matchesLeft == 0)
        {
            // Verify final state: partners hit each diff once, opponents hit each twice.
            for (int d = 1; d <= half; d++)
            {
                if (partnerSeen[d] != 1) return false;
                if (oppSeen[d] != 2) return false;
            }
            return true;
        }

        // Take the smallest remaining number as 'a' (canonicalize).
        var sorted = pool.OrderBy(x => x).ToList();
        int a = sorted[0];

        // Try each combination of 3 other elements as (b, c, d) with team (a,b) vs (c,d).
        // For each unordered choice of 3 elements, there are 3 ways to split into
        // partner b and opponents (c,d). We try all 3.
        for (int i = 1; i < sorted.Count; i++)
        for (int j = i + 1; j < sorted.Count; j++)
        for (int l = j + 1; l < sorted.Count; l++)
        {
            int[] others = { sorted[i], sorted[j], sorted[l] };
            // Three ways: partner = others[0], others[1], or others[2].
            for (int pIdx = 0; pIdx < 3; pIdx++)
            {
                int b = others[pIdx];
                int c = others[(pIdx + 1) % 3];
                int d = others[(pIdx + 2) % 3];

                int pd1 = NormDiff(a, b, m);
                int pd2 = NormDiff(c, d, m);
                int od1 = NormDiff(a, c, m);
                int od2 = NormDiff(a, d, m);
                int od3 = NormDiff(b, c, m);
                int od4 = NormDiff(b, d, m);

                if (partnerSeen[pd1] >= 1) continue;
                if (pd1 == pd2) continue; // would push partnerSeen[pd1] to 2
                if (partnerSeen[pd2] >= 1) continue;

                // Check opponent capacity feasibility before mutating.
                var localOpp = new int[] { od1, od2, od3, od4 };
                var oppDelta = new int[half + 1];
                bool ok = true;
                foreach (var od in localOpp)
                {
                    oppDelta[od]++;
                    if (oppSeen[od] + oppDelta[od] > 2) { ok = false; break; }
                }
                if (!ok) continue;

                partnerSeen[pd1]++;
                partnerSeen[pd2]++;
                foreach (var od in localOpp) oppSeen[od]++;

                var newPool = pool.Where(x => x != a && x != b && x != c && x != d).ToList();
                output.Add(new[] { a, b, c, d });

                if (SearchMatches(newPool, matchesLeft - 1, partnerSeen, oppSeen, half, m, output))
                    return true;

                output.RemoveAt(output.Count - 1);
                partnerSeen[pd1]--;
                partnerSeen[pd2]--;
                foreach (var od in localOpp) oppSeen[od]--;
            }
        }
        return false;
    }

    private static int NormDiff(int x, int y, int m)
    {
        int d = Math.Abs(x - y) % m;
        return Math.Min(d, m - d);
    }
}
