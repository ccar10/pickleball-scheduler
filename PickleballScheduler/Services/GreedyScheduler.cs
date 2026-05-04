using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public static class GreedyScheduler
{
    public static List<Round> Generate(List<Player> players, int courts, int rounds)
    {
        var matchesPerRound = Math.Min(courts, players.Count / 4);
        var playersPerRound = matchesPerRound * 4;

        var partnerCount = new Dictionary<string, int>();
        var opponentCount = new Dictionary<string, int>();
        var courtCount = players.ToDictionary(p => p.Id, _ => new int[matchesPerRound]);
        var byeCount = players.ToDictionary(p => p.Id, _ => 0);

        var output = new List<Round>(rounds);

        for (int r = 0; r < rounds; r++)
        {
            var active = SelectActive(players, playersPerRound, byeCount);
            var byes = players.Where(p => !active.Contains(p)).ToList();

            var matches = BuildRound(active, partnerCount, opponentCount, courtCount, matchesPerRound);

            UpdateCounters(matches, partnerCount, opponentCount, courtCount);
            foreach (var b in byes) byeCount[b.Id]++;

            output.Add(new Round
            {
                RoundNumber = r + 1,
                Matches = matches,
                Byes = byes.Select(p => new Bye { PlayerId = p.Id }).ToList()
            });
        }
        return output;
    }

    private static List<Player> SelectActive(List<Player> players, int needed, Dictionary<int, int> byeCount)
    {
        if (needed >= players.Count) return new List<Player>(players);
        return players
            .OrderByDescending(p => byeCount[p.Id])
            .ThenBy(p => p.Id)
            .Take(needed)
            .ToList();
    }

    private static List<Match> BuildRound(
        List<Player> active,
        Dictionary<string, int> partnerCount,
        Dictionary<string, int> opponentCount,
        Dictionary<int, int[]> courtCount,
        int matchesPerRound)
    {
        var used = new HashSet<int>();
        var seats = new List<(int a, int b, int c, int d)>(matchesPerRound);

        // Phase 1: pick partners and opponents for each match (no court yet).
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

            seats.Add((a.Id, b.Id, bestPair.c.Id, bestPair.d.Id));
            used.Add(a.Id);
            used.Add(b.Id);
            used.Add(bestPair.c.Id);
            used.Add(bestPair.d.Id);
        }

        // Phase 2: assign court labels by trying all matchesPerRound! permutations and picking the
        // one that minimizes the worst player's post-round court spread (sum of spreads as tiebreak).
        // matchesPerRound <= 6 in practice so the factorial is small (max 720).
        int[] bestAssignment = AssignCourts(seats, courtCount, matchesPerRound);

        var matches = new List<Match>(matchesPerRound);
        for (int i = 0; i < seats.Count; i++)
        {
            var s = seats[i];
            matches.Add(new Match
            {
                Team1Player1Id = s.a,
                Team1Player2Id = s.b,
                Team2Player1Id = s.c,
                Team2Player2Id = s.d,
                CourtNumber = bestAssignment[i] + 1,
            });
        }
        return matches;
    }

    private static int[] AssignCourts(
        List<(int a, int b, int c, int d)> seats,
        Dictionary<int, int[]> courtCount,
        int courts)
    {
        if (seats.Count <= 1) return new[] { 0 };

        var perm = Enumerable.Range(0, seats.Count).ToArray();
        var bestPerm = (int[])perm.Clone();
        long bestScore = long.MaxValue;

        do
        {
            long score = ScorePermutation(seats, perm, courtCount);
            if (score < bestScore)
            {
                bestScore = score;
                bestPerm = (int[])perm.Clone();
            }
        } while (NextPermutation(perm));

        return bestPerm;
    }

    private static long ScorePermutation(
        List<(int a, int b, int c, int d)> seats,
        int[] perm,
        Dictionary<int, int[]> courtCount)
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

        // Primary: minimize the worst player's spread. Secondary: minimize total.
        return maxSpread * 100_000L + sumSpread;
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

    private static void UpdateCounters(
        List<Match> matches,
        Dictionary<string, int> partnerCount,
        Dictionary<string, int> opponentCount,
        Dictionary<int, int[]> courtCount)
    {
        foreach (var m in matches)
        {
            partnerCount[PairKey(m.Team1Player1Id, m.Team1Player2Id)] =
                partnerCount.GetValueOrDefault(PairKey(m.Team1Player1Id, m.Team1Player2Id)) + 1;
            partnerCount[PairKey(m.Team2Player1Id, m.Team2Player2Id)] =
                partnerCount.GetValueOrDefault(PairKey(m.Team2Player1Id, m.Team2Player2Id)) + 1;

            foreach (var x in new[] { m.Team1Player1Id, m.Team1Player2Id })
                foreach (var y in new[] { m.Team2Player1Id, m.Team2Player2Id })
                    opponentCount[PairKey(x, y)] = opponentCount.GetValueOrDefault(PairKey(x, y)) + 1;

            int courtIdx = m.CourtNumber - 1;
            foreach (var pid in new[] { m.Team1Player1Id, m.Team1Player2Id, m.Team2Player1Id, m.Team2Player2Id })
                if (courtCount.ContainsKey(pid) && courtIdx < courtCount[pid].Length)
                    courtCount[pid][courtIdx]++;
        }
    }

    private static string PairKey(int a, int b) => a < b ? $"{a}-{b}" : $"{b}-{a}";
}
