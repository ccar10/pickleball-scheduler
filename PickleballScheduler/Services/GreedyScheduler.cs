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
    /// </summary>
    internal static List<Match> BuildOneRound(
        List<Player> active,
        Dictionary<string, int> partnerCount,
        Dictionary<string, int> opponentCount,
        int matchesPerRound)
    {
        var used = new HashSet<int>();
        var matches = new List<Match>(matchesPerRound);

        for (int slot = 0; slot < matchesPerRound; slot++)
        {
            var a = active.First(p => !used.Contains(p.Id));

            var b = active
                .Where(p => p.Id != a.Id && !used.Contains(p.Id))
                .OrderBy(p => partnerCount.GetValueOrDefault(PairKey(a.Id, p.Id)))
                .ThenBy(p => opponentCount.GetValueOrDefault(PairKey(a.Id, p.Id)))
                .ThenBy(p => p.Id)
                .First();

            var remaining = active.Where(p => p.Id != a.Id && p.Id != b.Id && !used.Contains(p.Id)).ToList();
            (Player c, Player d) bestPair = default;
            long bestScore = long.MaxValue;
            foreach (var pc in remaining)
            {
                foreach (var pd in remaining)
                {
                    if (pd.Id <= pc.Id) continue;
                    long score = 0;
                    foreach (var x in new[] { a.Id, b.Id })
                        foreach (var y in new[] { pc.Id, pd.Id })
                            score += opponentCount.GetValueOrDefault(PairKey(x, y));
                    score = score * 1000
                          + partnerCount.GetValueOrDefault(PairKey(pc.Id, pd.Id));
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestPair = (pc, pd);
                    }
                }
            }

            if (bestPair.c is null || bestPair.d is null)
                throw new InvalidOperationException(
                    $"No opponent pair found; active={active.Count}, used={used.Count}");

            matches.Add(new Match
            {
                Team1Player1Id = a.Id,
                Team1Player2Id = b.Id,
                Team2Player1Id = bestPair.c.Id,
                Team2Player2Id = bestPair.d.Id,
            });
            used.Add(a.Id);
            used.Add(b.Id);
            used.Add(bestPair.c.Id);
            used.Add(bestPair.d.Id);
        }

        return matches;
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
