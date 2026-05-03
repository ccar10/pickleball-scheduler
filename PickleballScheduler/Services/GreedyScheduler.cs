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
        var matches = new List<Match>(matchesPerRound);
        var unusedCourts = Enumerable.Range(0, matchesPerRound).ToList();

        while (unusedCourts.Count > 0)
        {
            // Pick player A: lowest-id unused active player.
            var a = active.First(p => !used.Contains(p.Id));

            // Pick partner B: minimize prior partner count with A; tiebreak by opp count, then id.
            var b = active
                .Where(p => p.Id != a.Id && !used.Contains(p.Id))
                .OrderBy(p => partnerCount.GetValueOrDefault(PairKey(a.Id, p.Id)))
                .ThenBy(p => opponentCount.GetValueOrDefault(PairKey(a.Id, p.Id)))
                .ThenBy(p => p.Id)
                .First();

            // Pick opponent pair (C, D): minimize sum of priorOpponentCount across the 4 cross-pairs.
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

            // Pick court: minimize sum of prior courtCount across the 4 players.
            int bestCourt = unusedCourts[0];
            int bestCourtScore = int.MaxValue;
            foreach (var courtIdx in unusedCourts)
            {
                int score = 0;
                foreach (var pid in new[] { a.Id, b.Id, bestPair.c.Id, bestPair.d.Id })
                    score += courtCount[pid][courtIdx];
                if (score < bestCourtScore)
                {
                    bestCourtScore = score;
                    bestCourt = courtIdx;
                }
            }

            matches.Add(new Match
            {
                Team1Player1Id = a.Id,
                Team1Player2Id = b.Id,
                Team2Player1Id = bestPair.c.Id,
                Team2Player2Id = bestPair.d.Id,
                CourtNumber = bestCourt + 1,
            });
            used.Add(a.Id); used.Add(b.Id); used.Add(bestPair.c.Id); used.Add(bestPair.d.Id);
            unusedCourts.Remove(bestCourt);
        }

        return matches;
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
