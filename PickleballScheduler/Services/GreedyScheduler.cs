using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

/// <summary>
/// Greedy round-by-round matchup picker plus shared court-permutation and counter-update helpers.
/// Used as a primary path for off-canonical configs and as a continuation for canonical configs
/// past round n-1 (after the Whist cycle is exhausted).
/// </summary>
public static class GreedyScheduler
{
    /// <summary>
    /// Top-level entry — produces a complete schedule by greedy matchup selection per round,
    /// followed by per-round court permutation. Used by the dispatcher when Whist isn't applicable.
    /// </summary>
    public static List<Round> Generate(List<Player> players, int courts, int rounds)
    {
        var matchesPerRound = Math.Min(courts, players.Count / 4);
        var partnerCount = new Dictionary<string, int>();
        var opponentCount = new Dictionary<string, int>();
        var courtCount = players.ToDictionary(p => p.Id, _ => new int[matchesPerRound]);
        var byeCount = players.ToDictionary(p => p.Id, _ => 0);
        var lastByeRound = new Dictionary<int, int>();

        var output = new List<Round>(rounds);

        for (int r = 0; r < rounds; r++)
        {
            var active = SelectActive(players, matchesPerRound * 4, byeCount, lastByeRound, r);
            var byes = players.Where(p => !active.Contains(p)).ToList();

            var matches = BuildOneRound(active, partnerCount, opponentCount, matchesPerRound);
            // For greedy matchups the structure changes round-to-round; the cyclic-shift tiebreak
            // (designed for symmetric Whist matchups) tends to make worse choices here, so disable it.
            AssignCourtsToRound(matches, courtCount, matchesPerRound, r, useCyclicShiftTiebreak: false);
            UpdateCounters(matches, partnerCount, opponentCount, courtCount);
            foreach (var b in byes) { byeCount[b.Id]++; lastByeRound[b.Id] = r; }

            output.Add(new Round
            {
                RoundNumber = r + 1,
                Matches = matches,
                Byes = byes.Select(p => new Bye { PlayerId = p.Id }).ToList()
            });
        }
        return output;
    }

    /// <summary>
    /// Bye rotation: pick byes by lowest total byes, then longest wait since last bye (max-spread).
    /// A rotational tiebreak (id*stride + round) prevents the all-tied case (e.g. round 0) from
    /// falling into a strict descending-ID pattern.
    /// </summary>
    internal static List<Player> SelectActive(
        List<Player> players,
        int needed,
        Dictionary<int, int> byeCount,
        Dictionary<int, int> lastByeRound,
        int currentRound)
    {
        if (needed >= players.Count) return new List<Player>(players);
        int numByes = players.Count - needed;
        int n = players.Count;
        int stride = StrideFor(n);

        var byeIds = players
            .OrderBy(p => byeCount[p.Id])
            .ThenBy(p => lastByeRound.GetValueOrDefault(p.Id, int.MinValue))
            .ThenBy(p => RotatedKey(p.Id, currentRound, stride, n))
            .ThenBy(p => p.Id)
            .Take(numByes)
            .Select(p => p.Id)
            .ToHashSet();

        return players.Where(p => !byeIds.Contains(p.Id)).ToList();
    }

    private static int RotatedKey(int playerId, int round, int stride, int n)
        => (int)((((long)playerId * stride + round) % n + n) % n);

    private static int StrideFor(int n)
    {
        for (int s = Math.Max(2, n / 2); s >= 2; s--)
            if (Gcd(s, n) == 1) return s;
        return 1;
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return Math.Abs(a);
    }

    /// <summary>
    /// Picks matches for one round given the current partner/opponent counts. Returns matches with
    /// no CourtNumber set — the caller is responsible for calling <see cref="AssignCourtsToRound"/>.
    ///
    /// Branch-and-bound over all valid round assignments (anchor fixed to lowest remaining id for
    /// symmetry breaking; (b,c,d) chosen from remaining; three team partitions tried per foursome).
    /// Scored by ((sum of (2k+1) over the round's partner pairs) * 1000) + (sum of opponent
    /// counts), where k = pre-existing partnerCount[pair]. The (2k+1) increment is the exact
    /// delta in sum-of-squared-partner-counts from adding the pair, so the round-level score is
    /// monotone in the global "squared partner count" objective — strongly discouraging triples.
    /// </summary>
    internal static List<Match> BuildOneRound(
        List<Player> active,
        Dictionary<string, int> partnerCount,
        Dictionary<string, int> opponentCount,
        int matchesPerRound)
    {
        if (matchesPerRound <= 0) return new List<Match>();
        var remaining = active.OrderBy(p => p.Id).Select(p => p.Id).ToList();
        if (remaining.Count < matchesPerRound * 4)
            throw new InvalidOperationException(
                $"BuildOneRound: active={remaining.Count} < matchesPerRound*4={matchesPerRound * 4}");

        // Quick-greedy baseline. Identical to the historical greedy, preserving its match-slot
        // rotation (which AssignCourtsToRound relies on for balancing per-player court visits).
        var initial = QuickGreedy(remaining, partnerCount, opponentCount, matchesPerRound);

        // Compute admissible lower bound on the round's partner cost: the sum of (2k+1) over the
        // 2*matchesPerRound smallest partner counts among active pairs. Any valid matching's
        // partner cost is >= this bound, so if greedy already hits it, greedy is partner-optimal
        // and B&B can't improve. Skipping B&B in that case preserves the rotational structure of
        // greedy's output, which AssignCourtsToRound depends on; running B&B unnecessarily can
        // shuffle equivalently-scored arrangements into slot patterns that produce bad court
        // spread for individual players.
        long initialPartnerCost = ComputePartnerCost(initial, partnerCount);
        long partnerLowerBound = ComputePartnerLowerBound(remaining, partnerCount, matchesPerRound);

        var best = new SearchState
        {
            BestMatches = initial,
            BestScore = ScoreMatches(initial, partnerCount, opponentCount),
        };

        if (initialPartnerCost > partnerLowerBound)
        {
            var current = new List<(int, int, int, int)>(matchesPerRound);
            Search(remaining, current, 0, 0, matchesPerRound, partnerCount, opponentCount, best);
        }

        return best.BestMatches!.Select(m => new Match
        {
            Team1Player1Id = m.Item1,
            Team1Player2Id = m.Item2,
            Team2Player1Id = m.Item3,
            Team2Player2Id = m.Item4,
        }).ToList();
    }

    private sealed class SearchState
    {
        public List<(int, int, int, int)>? BestMatches;
        public long BestScore = long.MaxValue;
        public int NodesExplored;
    }

    private const long PartnerWeight = 1000L;
    // Bounds B&B exploration in pathological configs (large active sets, no clear winner).
    // For realistic inputs (active <= 12) the search finishes long before this. When the
    // budget runs out, BuildOneRound returns the best leaf found so far — at worst the
    // QuickGreedy seed, never an invalid matchup.
    private const int MaxSearchNodes = 250_000;

    private static long ScoreMatches(
        List<(int, int, int, int)> matches,
        Dictionary<string, int> partnerCount,
        Dictionary<string, int> opponentCount)
    {
        long partnerCost = ComputePartnerCost(matches, partnerCount);
        long oppCost = 0;
        foreach (var m in matches)
        {
            oppCost += opponentCount.GetValueOrDefault(PairKey(m.Item1, m.Item3));
            oppCost += opponentCount.GetValueOrDefault(PairKey(m.Item1, m.Item4));
            oppCost += opponentCount.GetValueOrDefault(PairKey(m.Item2, m.Item3));
            oppCost += opponentCount.GetValueOrDefault(PairKey(m.Item2, m.Item4));
        }
        return partnerCost * PartnerWeight + oppCost;
    }

    private static long ComputePartnerCost(
        List<(int, int, int, int)> matches,
        Dictionary<string, int> partnerCount)
    {
        long c = 0;
        foreach (var m in matches)
        {
            c += 2L * partnerCount.GetValueOrDefault(PairKey(m.Item1, m.Item2)) + 1;
            c += 2L * partnerCount.GetValueOrDefault(PairKey(m.Item3, m.Item4)) + 1;
        }
        return c;
    }

    private static long ComputePartnerLowerBound(
        List<int> active,
        Dictionary<string, int> partnerCount,
        int matchesPerRound)
    {
        int needed = matchesPerRound * 2;
        var counts = new List<int>(active.Count * (active.Count - 1) / 2);
        for (int i = 0; i < active.Count; i++)
            for (int j = i + 1; j < active.Count; j++)
                counts.Add(partnerCount.GetValueOrDefault(PairKey(active[i], active[j])));
        counts.Sort();

        long lb = 0;
        for (int k = 0; k < needed && k < counts.Count; k++)
            lb += 2L * counts[k] + 1;
        return lb;
    }

    private static List<(int, int, int, int)> QuickGreedy(
        List<int> remaining,
        Dictionary<string, int> partnerCount,
        Dictionary<string, int> opponentCount,
        int matchesPerRound)
    {
        var available = new List<int>(remaining);
        var matches = new List<(int, int, int, int)>(matchesPerRound);

        for (int slot = 0; slot < matchesPerRound; slot++)
        {
            int a = available[0];

            // Partner b: min partner cost (with opp count as tiebreak).
            int chosenB = -1;
            long bestScore = long.MaxValue;
            for (int i = 1; i < available.Count; i++)
            {
                int candidate = available[i];
                int pc = partnerCount.GetValueOrDefault(PairKey(a, candidate));
                int oc = opponentCount.GetValueOrDefault(PairKey(a, candidate));
                long score = (long)pc * 1000 + oc;
                if (score < bestScore) { bestScore = score; chosenB = candidate; }
            }
            int b = chosenB;

            // Opponent pair (c, d): minimize combined opp cost; partner cost of (c,d) tiebreak.
            int chosenC = -1, chosenD = -1;
            long bestPairScore = long.MaxValue;
            for (int i = 1; i < available.Count; i++)
            {
                if (available[i] == b) continue;
                for (int j = i + 1; j < available.Count; j++)
                {
                    if (available[j] == b) continue;
                    int c = available[i], d = available[j];
                    long oppCost = opponentCount.GetValueOrDefault(PairKey(a, c))
                                 + opponentCount.GetValueOrDefault(PairKey(a, d))
                                 + opponentCount.GetValueOrDefault(PairKey(b, c))
                                 + opponentCount.GetValueOrDefault(PairKey(b, d));
                    int cdPart = partnerCount.GetValueOrDefault(PairKey(c, d));
                    long score = oppCost * 1000 + cdPart;
                    if (score < bestPairScore) { bestPairScore = score; chosenC = c; chosenD = d; }
                }
            }

            matches.Add((a, b, chosenC, chosenD));
            available.Remove(a);
            available.Remove(b);
            available.Remove(chosenC);
            available.Remove(chosenD);
        }
        return matches;
    }

    private static void Search(
        List<int> remaining,
        List<(int, int, int, int)> current,
        long partnerCostSoFar,
        long opponentCostSoFar,
        int matchesPerRound,
        Dictionary<string, int> partnerCount,
        Dictionary<string, int> opponentCount,
        SearchState best)
    {
        if (best.NodesExplored >= MaxSearchNodes) return;
        best.NodesExplored++;

        if (current.Count == matchesPerRound)
        {
            long score = partnerCostSoFar * PartnerWeight + opponentCostSoFar;
            if (score < best.BestScore)
            {
                best.BestScore = score;
                best.BestMatches = new List<(int, int, int, int)>(current);
            }
            return;
        }

        // Symmetry breaking: anchor is the lowest remaining id.
        int a = remaining[0];

        for (int ib = 1; ib < remaining.Count; ib++)
        for (int ic = ib + 1; ic < remaining.Count; ic++)
        for (int id = ic + 1; id < remaining.Count; id++)
        {
            int b = remaining[ib];
            int c = remaining[ic];
            int d = remaining[id];

            // Three team partitions of foursome {a,b,c,d}.
            TryPartition(a, b, c, d, remaining, ib, ic, id, current,
                         partnerCostSoFar, opponentCostSoFar,
                         matchesPerRound, partnerCount, opponentCount, best);
            TryPartition(a, c, b, d, remaining, ib, ic, id, current,
                         partnerCostSoFar, opponentCostSoFar,
                         matchesPerRound, partnerCount, opponentCount, best);
            TryPartition(a, d, b, c, remaining, ib, ic, id, current,
                         partnerCostSoFar, opponentCostSoFar,
                         matchesPerRound, partnerCount, opponentCount, best);
        }
    }

    private static void TryPartition(
        int t1a, int t1b, int t2a, int t2b,
        List<int> remaining, int ib, int ic, int id,
        List<(int, int, int, int)> current,
        long partnerCostSoFar, long opponentCostSoFar,
        int matchesPerRound,
        Dictionary<string, int> partnerCount,
        Dictionary<string, int> opponentCount,
        SearchState best)
    {
        // Partner cost delta = (2k + 1) per added pair, where k = pre-existing partner count.
        int pkA = partnerCount.GetValueOrDefault(PairKey(t1a, t1b));
        int pkB = partnerCount.GetValueOrDefault(PairKey(t2a, t2b));
        long deltaPartner = (2L * pkA + 1) + (2L * pkB + 1);

        long newPartnerCost = partnerCostSoFar + deltaPartner;

        long deltaOpp = opponentCount.GetValueOrDefault(PairKey(t1a, t2a))
                      + opponentCount.GetValueOrDefault(PairKey(t1a, t2b))
                      + opponentCount.GetValueOrDefault(PairKey(t1b, t2a))
                      + opponentCount.GetValueOrDefault(PairKey(t1b, t2b));
        long newOpponentCost = opponentCostSoFar + deltaOpp;

        // Tight admissible lower bound: each remaining slot must contribute at least 2 partner
        // cost units (2 pairs * 1 each). Future opp contribution >= 0.
        int newDepth = current.Count + 1;
        long futurePartnerLB = 2L * (matchesPerRound - newDepth);
        long lb = (newPartnerCost + futurePartnerLB) * PartnerWeight + newOpponentCost;
        if (lb >= best.BestScore) return;

        current.Add((t1a, t1b, t2a, t2b));
        var newRemaining = new List<int>(remaining.Count - 4);
        for (int k = 1; k < remaining.Count; k++)
            if (k != ib && k != ic && k != id) newRemaining.Add(remaining[k]);

        Search(newRemaining, current, newPartnerCost, newOpponentCost,
               matchesPerRound, partnerCount, opponentCount, best);
        current.RemoveAt(current.Count - 1);
    }

    /// <summary>
    /// Sets <see cref="Match.CourtNumber"/> on each match by trying all matchesPerRound!
    /// permutations of court indices and choosing the one that minimizes the worst player's
    /// post-round court spread. matchesPerRound &lt;= 6 in practice so the factorial is small (max 720).
    ///
    /// When permutations tie on cost (common for symmetric Whist matchups, e.g. Wh(8) where the
    /// "infinity" player sits in match 0 every round), the tiebreak prefers a cyclic-shift
    /// assignment driven by <paramref name="roundIndex"/>, so a player consistently in the same
    /// match position cycles through all courts rather than getting stuck on one.
    /// </summary>
    internal static void AssignCourtsToRound(
        List<Match> matches,
        Dictionary<int, int[]> courtCount,
        int matchesPerRound,
        int roundIndex,
        bool useCyclicShiftTiebreak)
    {
        if (matches.Count == 0) return;

        if (matches.Count == 1)
        {
            matches[0].CourtNumber = 1;
            return;
        }

        var seats = matches
            .Select(m => (m.Team1Player1Id, m.Team1Player2Id, m.Team2Player1Id, m.Team2Player2Id))
            .ToList();

        var perm = Enumerable.Range(0, seats.Count).ToArray();
        var bestPerm = (int[])perm.Clone();
        long bestScore = long.MaxValue;

        do
        {
            long score = ScorePermutation(seats, perm, courtCount, roundIndex, useCyclicShiftTiebreak);
            if (score < bestScore)
            {
                bestScore = score;
                bestPerm = (int[])perm.Clone();
            }
        } while (NextPermutation(perm));

        for (int i = 0; i < matches.Count; i++)
        {
            matches[i].CourtNumber = bestPerm[i] + 1;
        }
    }

    /// <summary>
    /// Mutates partner, opponent, and court counters to reflect a completed round.
    /// </summary>
    internal static void UpdateCounters(
        List<Match> matches,
        Dictionary<string, int> partnerCount,
        Dictionary<string, int> opponentCount,
        Dictionary<int, int[]> courtCount)
    {
        foreach (var m in matches)
        {
            var pk1 = PairKey(m.Team1Player1Id, m.Team1Player2Id);
            partnerCount[pk1] = partnerCount.GetValueOrDefault(pk1) + 1;
            var pk2 = PairKey(m.Team2Player1Id, m.Team2Player2Id);
            partnerCount[pk2] = partnerCount.GetValueOrDefault(pk2) + 1;

            foreach (var x in new[] { m.Team1Player1Id, m.Team1Player2Id })
                foreach (var y in new[] { m.Team2Player1Id, m.Team2Player2Id })
                {
                    var ok = PairKey(x, y);
                    opponentCount[ok] = opponentCount.GetValueOrDefault(ok) + 1;
                }

            int courtIdx = m.CourtNumber - 1;
            foreach (var pid in new[] { m.Team1Player1Id, m.Team1Player2Id, m.Team2Player1Id, m.Team2Player2Id })
                if (courtCount.ContainsKey(pid) && courtIdx < courtCount[pid].Length)
                    courtCount[pid][courtIdx]++;
        }
    }

    private static long ScorePermutation(
        List<(int a, int b, int c, int d)> seats,
        int[] perm,
        Dictionary<int, int[]> courtCount,
        int roundIndex,
        bool useCyclicShiftTiebreak)
    {
        long maxSpread = 0;
        long sumSpread = 0;

        for (int i = 0; i < seats.Count; i++)
        {
            var s = seats[i];
            int courtIdx = perm[i];
            foreach (var pid in new[] { s.a, s.b, s.c, s.d })
            {
                if (!courtCount.TryGetValue(pid, out var counts) || courtIdx >= counts.Length) continue;
                int max = 0, min = int.MaxValue;
                for (int ci = 0; ci < counts.Length; ci++)
                {
                    int v = counts[ci] + (ci == courtIdx ? 1 : 0);
                    if (v > max) max = v;
                    if (v < min) min = v;
                }
                int spread = max - min;
                sumSpread += spread;
                if (spread > maxSpread) maxSpread = spread;
            }
        }

        long shiftDistance = 0;
        if (useCyclicShiftTiebreak)
        {
            // Tiebreak: prefer perms close to the cyclic shift (i+r) mod c. For symmetric matchups
            // (e.g. Wh(8) where one player sits in match 0 every round), this rotates which court
            // match 0 plays on each round so the always-in-match-0 player cycles through courts.
            long courts = perm.Length;
            for (int i = 0; i < perm.Length; i++)
            {
                int preferred = (int)(((i + roundIndex) % courts + courts) % courts);
                int diff = Math.Abs(perm[i] - preferred);
                if (diff > courts / 2) diff = (int)courts - diff;
                shiftDistance += diff;
            }
        }

        return maxSpread * 1_000_000L + sumSpread * 1_000L + shiftDistance;
    }

    private static bool NextPermutation(int[] arr)
    {
        int i = arr.Length - 2;
        while (i >= 0 && arr[i] >= arr[i + 1]) i--;
        if (i < 0) return false;
        int j = arr.Length - 1;
        while (arr[j] <= arr[i]) j--;
        (arr[i], arr[j]) = (arr[j], arr[i]);
        Array.Reverse(arr, i + 1, arr.Length - 1 - i);
        return true;
    }

    internal static string PairKey(int a, int b) => a < b ? $"{a}-{b}" : $"{b}-{a}";
}
