using System.Diagnostics;
using System.Text;
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
        return EnumerateValidBaseRounds(n).FirstOrDefault();
    }

    /// <summary>
    /// Enumerates valid Whist base rounds for Wh(n). Each yielded value is a list
    /// of match descriptors as in <see cref="BruteForceBaseRound"/>. The first
    /// yielded element is the same as <see cref="BruteForceBaseRound"/>'s return.
    /// </summary>
    internal static IEnumerable<List<string[]>> EnumerateValidBaseRounds(int n)
    {
        int k = n / 4;
        int m = n - 1;
        int half = m / 2;

        for (int infA = 0; infA < m; infA++)
        {
            for (int infB = 0; infB < m; infB++)
            {
                if (infB == infA) continue;
                for (int infC = infB + 1; infC < m; infC++)
                {
                    if (infC == infA) continue;
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

                    var output = new List<int[]>();
                    foreach (var _ in EnumerateInner(remaining, k - 1, partnerSeen, oppSeen, half, m, output))
                    {
                        var result = new List<string[]>
                        {
                            new[] { "inf", infA.ToString(), infB.ToString(), infC.ToString() }
                        };
                        foreach (var match in output)
                            result.Add(new[]
                            {
                                match[0].ToString(), match[1].ToString(),
                                match[2].ToString(), match[3].ToString(),
                            });
                        yield return result;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Generator equivalent of <c>SearchMatches</c>. Yields a sentinel value (true)
    /// each time a complete valid arrangement is reached; on each yield, <paramref name="output"/>
    /// holds the current valid match list. Caller must read it before continuing iteration.
    /// </summary>
    private static IEnumerable<bool> EnumerateInner(List<int> pool, int matchesLeft,
        int[] partnerSeen, int[] oppSeen, int half, int m,
        List<int[]> output)
    {
        if (matchesLeft == 0)
        {
            for (int d = 1; d <= half; d++)
            {
                if (partnerSeen[d] != 1) yield break;
                if (oppSeen[d] != 2) yield break;
            }
            yield return true;
            yield break;
        }

        var sorted = pool.OrderBy(x => x).ToList();
        int a = sorted[0];

        for (int i = 1; i < sorted.Count; i++)
        for (int j = i + 1; j < sorted.Count; j++)
        for (int l = j + 1; l < sorted.Count; l++)
        {
            int[] others = { sorted[i], sorted[j], sorted[l] };
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
                if (pd1 == pd2) continue;
                if (partnerSeen[pd2] >= 1) continue;

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

                foreach (var sentinel in EnumerateInner(newPool, matchesLeft - 1, partnerSeen, oppSeen, half, m, output))
                    yield return sentinel;

                output.RemoveAt(output.Count - 1);
                partnerSeen[pd1]--;
                partnerSeen[pd2]--;
                foreach (var od in localOpp) oppSeen[od]--;
            }
        }
    }

    private static int NormDiff(int x, int y, int m)
    {
        int d = Math.Abs(x - y) % m;
        return Math.Min(d, m - d);
    }

    // ----- Joint (base round, court labels) search -----

    /// <summary>
    /// One-shot joint generator: enumerates Whist base rounds, and for each tries
    /// to find a per-round court labeling such that every player's court-visit
    /// spread is ≤ 1. Spends up to 30 minutes wall-clock per size. On success,
    /// emits a paste-ready C# transcript via <see cref="Assert.Fail"/>; on budget
    /// exhaustion, fails with a "no balanced pair found" message.
    /// Re-skip after harvesting outputs.
    /// </summary>
    [Theory(Skip = "one-shot generator; remove Skip to regenerate balanced (base round, court labels)")]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    public void GenerateBalancedSchedule(int playerCount)
    {
        int n = playerCount;
        int courts = n / 4;
        int rounds = n - 1;
        int ceiling = (rounds + courts - 1) / courts; // ceil((n-1) / (n/4))
        var perms = AllPermutations(courts);

        var budget = TimeSpan.FromMinutes(30);
        var sw = Stopwatch.StartNew();
        int candidatesTried = 0;

        foreach (var baseRound in EnumerateValidBaseRounds(n))
        {
            if (sw.Elapsed >= budget) break;
            candidatesTried++;

            var matchups = BuildAllRoundMatchups(baseRound, n);

            // Per-player per-court visit counts; player ids are 1..n.
            var visits = new Dictionary<int, int[]>();
            for (int pid = 1; pid <= n; pid++) visits[pid] = new int[courts];

            var labels = new int[rounds][];
            for (int r = 0; r < rounds; r++) labels[r] = new int[courts];

            if (TrySearchWithBudget(0, matchups, perms, visits, ceiling, labels, sw, budget))
            {
                EmitTranscript(n, baseRound, labels, candidatesTried, sw.Elapsed);
                return;
            }
        }

        Assert.Fail(
            $"Wh({n}): no balanced (base round, court labels) pair found within budget. " +
            $"Tried {candidatesTried} base rounds; elapsed {sw.Elapsed.TotalMinutes:F1} min.");
    }

    /// <summary>
    /// Builds the full <c>n-1</c> rounds of matchups from a base round descriptor.
    /// Each Match is represented as a 4-tuple of player ids; player 1 is "inf",
    /// players 2..n are roles 0..n-2 under cyclic rotation mod n-1.
    /// </summary>
    private static List<List<(int p0, int p1, int p2, int p3)>> BuildAllRoundMatchups(
        List<string[]> baseRound, int n)
    {
        var rotateMod = n - 1;
        var rounds = new List<List<(int, int, int, int)>>(n - 1);
        for (int r = 0; r < n - 1; r++)
        {
            var matches = new List<(int, int, int, int)>(baseRound.Count);
            foreach (var bm in baseRound)
            {
                int Resolve(string role) =>
                    role == "inf" ? 1 : 2 + ((int.Parse(role) + r) % rotateMod);
                matches.Add((Resolve(bm[0]), Resolve(bm[1]), Resolve(bm[2]), Resolve(bm[3])));
            }
            rounds.Add(matches);
        }
        return rounds;
    }

    private static bool TrySearchWithBudget(int round,
        List<List<(int p0, int p1, int p2, int p3)>> matchups,
        List<int[]> perms, Dictionary<int, int[]> visits, int ceiling, int[][] labels,
        Stopwatch sw, TimeSpan budget)
    {
        if (sw.Elapsed >= budget) return false;

        if (round == matchups.Count)
        {
            foreach (var counts in visits.Values)
                if (counts.Max() - counts.Min() > 1) return false;
            return true;
        }

        var matches = matchups[round];

        foreach (var perm in OrderPermutations(perms, matches, visits))
        {
            if (sw.Elapsed >= budget) return false;

            var applied = new List<(int pid, int ci)>();
            bool ok = true;
            for (int i = 0; i < matches.Count && ok; i++)
            {
                var ci = perm[i];
                foreach (var pid in PlayerIds(matches[i]))
                {
                    visits[pid][ci]++;
                    applied.Add((pid, ci));
                    if (visits[pid][ci] > ceiling) ok = false;
                }
            }

            if (ok)
            {
                Array.Copy(perm, labels[round], perm.Length);
                if (TrySearchWithBudget(round + 1, matchups, perms, visits, ceiling, labels, sw, budget)) return true;
            }

            foreach (var (pid, ci) in applied) visits[pid][ci]--;
        }

        return false;
    }

    private static List<int[]> AllPermutations(int n)
    {
        var result = new List<int[]>();
        var current = Enumerable.Range(0, n).ToArray();
        PermuteRecursive(current, 0, result);
        return result;
    }

    private static void PermuteRecursive(int[] arr, int start, List<int[]> result)
    {
        if (start == arr.Length)
        {
            result.Add((int[])arr.Clone());
            return;
        }
        for (int i = start; i < arr.Length; i++)
        {
            (arr[start], arr[i]) = (arr[i], arr[start]);
            PermuteRecursive(arr, start + 1, result);
            (arr[start], arr[i]) = (arr[i], arr[start]);
        }
    }

    private static IEnumerable<int> PlayerIds((int p0, int p1, int p2, int p3) match)
    {
        yield return match.p0;
        yield return match.p1;
        yield return match.p2;
        yield return match.p3;
    }

    /// <summary>
    /// Heuristic: try permutations in order of how well they balance the most
    /// disadvantaged player (greatest current spread). Best (lowest spread-after)
    /// first.
    /// </summary>
    private static IEnumerable<int[]> OrderPermutations(List<int[]> perms,
        List<(int p0, int p1, int p2, int p3)> matches, Dictionary<int, int[]> visits)
    {
        return perms.OrderBy(perm => ScorePerm(perm, matches, visits));
    }

    private static long ScorePerm(int[] perm,
        List<(int p0, int p1, int p2, int p3)> matches, Dictionary<int, int[]> visits)
    {
        long sum = 0;
        for (int i = 0; i < matches.Count; i++)
        {
            var ci = perm[i];
            foreach (var pid in PlayerIds(matches[i]))
            {
                // Cost = current count on the court we'd put this player on.
                // Lower is better — bias toward courts where the player has been least.
                sum += visits[pid][ci];
            }
        }
        return sum;
    }

    private static void EmitTranscript(int n, List<string[]> baseRound, int[][] labels,
        int candidatesTried, TimeSpan elapsed)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// Wh({n}) — balanced (base round, court labels) pair found.");
        sb.AppendLine($"// Tried {candidatesTried} base rounds in {elapsed.TotalMinutes:F2} minutes.");
        sb.AppendLine();
        sb.AppendLine($"// Replace BaseRounds[{n}] with:");
        sb.AppendLine($"[{n}] = new[]");
        sb.AppendLine("{");
        foreach (var m in baseRound)
            sb.AppendLine($"    new BaseMatch(\"{m[0]}\", \"{m[1]}\", \"{m[2]}\", \"{m[3]}\"),");
        sb.AppendLine("},");
        sb.AppendLine();
        sb.AppendLine($"// Add to CourtLabels[{n}]:");
        sb.AppendLine($"[{n}] = new int[][]");
        sb.AppendLine("{");
        foreach (var row in labels)
            sb.AppendLine($"    new int[] {{ {string.Join(", ", row)} }},");
        sb.AppendLine("},");

        Assert.Fail(sb.ToString());
    }
}
