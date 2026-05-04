using System.Text;

namespace PickleballScheduler.Tests.Services;

public class CanonicalScheduleGenerator
{
    private const int RoundCount = 30;
    private const int InitialSeed = 20260503;

    [Theory(Skip = "one-shot generator; un-skip the row you want to regenerate, copy the Assert.Fail output into CanonicalSchedules.cs Schedules dictionary")]
    [InlineData(8, 2)]
    [InlineData(12, 3)]
    [InlineData(16, 4)]
    [InlineData(20, 5)]
    [InlineData(24, 6)]
    public void Generate(int n, int courts)
    {
        var rng = new Random(InitialSeed + n);

        // Schedule[round] = matches in that round; each match is (a, b, c, d) role indices and a court 0..courts-1.
        var schedule = RandomInitialSchedule(n, courts, rng);
        long currentCost = ComputeCost(schedule, n, courts);
        var bestSchedule = Clone(schedule);
        long bestCost = currentCost;

        double temperature = 100.0;
        const double cooling = 0.9999;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var budget = TimeSpan.FromSeconds(60);

        // Note: bestCost == 0 is unreachable for these sizes (non-integer ideal partner counts
        // mean partnerSpread >= 1 always), so the budget governs termination in practice.
        while (sw.Elapsed < budget && bestCost > 0)
        {
            var candidate = Clone(schedule);
            ApplyLocalMove(candidate, n, courts, rng);
            long candidateCost = ComputeCost(candidate, n, courts);
            long delta = candidateCost - currentCost;

            if (delta < 0 || rng.NextDouble() < Math.Exp(-delta / Math.Max(temperature, 0.01)))
            {
                schedule = candidate;
                currentCost = candidateCost;
                if (candidateCost < bestCost)
                {
                    bestCost = candidateCost;
                    bestSchedule = Clone(candidate);
                }
            }
            temperature *= cooling;
        }

        // Emit paste-ready initializer for `[<n>] = ...` in CanonicalSchedules.Schedules.
        var sb = new StringBuilder();
        sb.AppendLine($"// ({n}, {courts}) — final cost {bestCost}");
        sb.AppendLine($"[{n}] = new BakedMatch[][]");
        sb.AppendLine("{");
        foreach (var round in bestSchedule)
        {
            sb.Append("    new[] { ");
            sb.Append(string.Join(", ", round.Select(m =>
                $"new BakedMatch({m.A}, {m.B}, {m.C}, {m.D}, {m.Court})")));
            sb.AppendLine(" },");
        }
        sb.AppendLine("},");

        Assert.Fail(sb.ToString());
    }

    private record struct BakedMatchRow(int A, int B, int C, int D, int Court);

    private static List<List<BakedMatchRow>> RandomInitialSchedule(int n, int courts, Random rng)
    {
        var schedule = new List<List<BakedMatchRow>>(RoundCount);
        for (int r = 0; r < RoundCount; r++)
        {
            var perm = Enumerable.Range(0, n).OrderBy(_ => rng.Next()).ToArray();
            var round = new List<BakedMatchRow>(courts);
            for (int c = 0; c < courts; c++)
            {
                int o = c * 4;
                round.Add(new BakedMatchRow(perm[o], perm[o + 1], perm[o + 2], perm[o + 3], c));
            }
            schedule.Add(round);
        }
        return schedule;
    }

    private static List<List<BakedMatchRow>> Clone(List<List<BakedMatchRow>> s)
    {
        var copy = new List<List<BakedMatchRow>>(s.Count);
        foreach (var r in s) copy.Add(new List<BakedMatchRow>(r));
        return copy;
    }

    private static void ApplyLocalMove(List<List<BakedMatchRow>> s, int n, int courts, Random rng)
    {
        var pick = rng.NextDouble();
        if (pick < 0.6)
        {
            // Swap two players within the same round.
            int r = rng.Next(s.Count);
            var round = s[r];
            int idxA = rng.Next(courts);
            int idxB = rng.Next(courts);
            int slotA = rng.Next(4);
            int slotB = rng.Next(4);
            if (idxA == idxB && slotA == slotB) return;

            int va = GetSlot(round[idxA], slotA);
            int vb = GetSlot(round[idxB], slotB);
            round[idxA] = SetSlot(round[idxA], slotA, vb);
            round[idxB] = SetSlot(round[idxB], slotB, va);
        }
        else if (pick < 0.85)
        {
            // Swap court labels of two matches in the same round.
            int r = rng.Next(s.Count);
            var round = s[r];
            int i = rng.Next(courts);
            int j = rng.Next(courts);
            if (i == j) return;
            (round[i], round[j]) = (
                round[i] with { Court = round[j].Court },
                round[j] with { Court = round[i].Court });
        }
        else
        {
            // Swap two whole rounds.
            int i = rng.Next(s.Count);
            int j = rng.Next(s.Count);
            if (i == j) return;
            (s[i], s[j]) = (s[j], s[i]);
        }
    }

    private static int GetSlot(BakedMatchRow m, int slot) => slot switch
    {
        0 => m.A, 1 => m.B, 2 => m.C, 3 => m.D, _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };

    private static BakedMatchRow SetSlot(BakedMatchRow m, int slot, int v) => slot switch
    {
        0 => m with { A = v },
        1 => m with { B = v },
        2 => m with { C = v },
        3 => m with { D = v },
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    private static long ComputeCost(List<List<BakedMatchRow>> s, int n, int courts)
    {
        // Validate: every player appears exactly once per round, every court 0..courts-1 used once per round.
        // Invalid candidates get cost long.MaxValue / 4 to push the search away.
        foreach (var round in s)
        {
            var seenPlayers = new HashSet<int>();
            var seenCourts = new HashSet<int>();
            foreach (var m in round)
            {
                if (!seenPlayers.Add(m.A) || !seenPlayers.Add(m.B)
                    || !seenPlayers.Add(m.C) || !seenPlayers.Add(m.D)) return long.MaxValue / 4;
                if (!seenCourts.Add(m.Court)) return long.MaxValue / 4;
            }
            if (seenPlayers.Count != n) return long.MaxValue / 4;
        }

        // Component 1: consecutive partner repeats.
        int consecutive = 0;
        var prevPairs = new HashSet<(int, int)>();
        for (int r = 0; r < s.Count; r++)
        {
            var current = new HashSet<(int, int)>();
            foreach (var m in s[r])
            {
                current.Add(Pair(m.A, m.B));
                current.Add(Pair(m.C, m.D));
            }
            if (r > 0) consecutive += prevPairs.Intersect(current).Count();
            prevPairs = current;
        }

        // Component 2: partner spread (max - min over pair counts).
        var partnerCount = new Dictionary<(int, int), int>();
        foreach (var round in s)
            foreach (var m in round)
            {
                Inc(partnerCount, m.A, m.B);
                Inc(partnerCount, m.C, m.D);
            }
        int partnerSpread = partnerCount.Count == 0 ? 0
            : partnerCount.Values.Max() - partnerCount.Values.Min();

        // Component 3: court-visit spread per player.
        var courtCount = new int[n, courts];
        foreach (var round in s)
            foreach (var m in round)
            {
                courtCount[m.A, m.Court]++;
                courtCount[m.B, m.Court]++;
                courtCount[m.C, m.Court]++;
                courtCount[m.D, m.Court]++;
            }
        int courtSpread = 0;
        for (int p = 0; p < n; p++)
        {
            int max = 0, min = int.MaxValue;
            for (int c = 0; c < courts; c++)
            {
                if (courtCount[p, c] > max) max = courtCount[p, c];
                if (courtCount[p, c] < min) min = courtCount[p, c];
            }
            courtSpread += max - min;
        }

        // Component 4: opponent count variance.
        var opponentCount = new Dictionary<(int, int), int>();
        foreach (var round in s)
            foreach (var m in round)
                foreach (var x in new[] { m.A, m.B })
                    foreach (var y in new[] { m.C, m.D })
                        Inc(opponentCount, x, y);
        long oppVariance = 0;
        if (opponentCount.Count > 0)
        {
            double mean = opponentCount.Values.Average();
            foreach (var v in opponentCount.Values)
            {
                long diff = (long)Math.Round((v - mean) * 1000);
                oppVariance += diff * diff / 1_000_000;
            }
        }

        return (long)consecutive * 1_000_000_000L
             + (long)partnerSpread * partnerSpread * 1_000_000L
             + (long)courtSpread * courtSpread * 1_000L
             + oppVariance;
    }

    private static (int, int) Pair(int a, int b) => a < b ? (a, b) : (b, a);

    private static void Inc(Dictionary<(int, int), int> d, int a, int b)
    {
        var k = Pair(a, b);
        d[k] = d.GetValueOrDefault(k) + 1;
    }
}
