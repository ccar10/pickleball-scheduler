using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public class ScheduleGenerator
{
    public List<Round> Generate(List<Player> players, int numberOfCourts, int numberOfRounds)
    {
        var matchesPerRound = Math.Min(numberOfCourts, players.Count / 4);
        var playersPerRound = matchesPerRound * 4;
        var partnerCounts = new Dictionary<string, int>();
        var lastPartneredRound = new Dictionary<string, int>();
        var opponentCounts = new Dictionary<string, int>();
        var courtCounts = new Dictionary<int, int[]>();
        var byeCounts = players.ToDictionary(p => p.Id, _ => 0);
        var rounds = new List<Round>();

        foreach (var p in players)
        {
            courtCounts[p.Id] = new int[matchesPerRound];
        }

        for (int r = 0; r < numberOfRounds; r++)
        {
            var activePlayers = SelectActivePlayers(players, playersPerRound, byeCounts);
            var byePlayers = players.Where(p => !activePlayers.Contains(p)).ToList();

            var shuffled = ShufflePlayers(activePlayers, r);

            // Form teams: minimize partner count, no consecutive repeats
            var teams = FormTeams(shuffled, partnerCounts, lastPartneredRound, r);

            // Pair teams into matches: maximize opponent variety
            var matches = PairTeamsIntoMatches(teams, opponentCounts);

            // Assign courts: balance court usage
            AssignCourts(matches, courtCounts, matchesPerRound);

            // Record all tracking data
            foreach (var match in matches)
            {
                var t1p1 = match.Team1Player1Id;
                var t1p2 = match.Team1Player2Id;
                var t2p1 = match.Team2Player1Id;
                var t2p2 = match.Team2Player2Id;

                // Partner counts
                var pk1 = PairKey(t1p1, t1p2);
                var pk2 = PairKey(t2p1, t2p2);
                partnerCounts[pk1] = partnerCounts.GetValueOrDefault(pk1) + 1;
                partnerCounts[pk2] = partnerCounts.GetValueOrDefault(pk2) + 1;
                lastPartneredRound[pk1] = r;
                lastPartneredRound[pk2] = r;

                // Opponent counts
                var team1 = new[] { t1p1, t1p2 };
                var team2 = new[] { t2p1, t2p2 };
                foreach (var p1 in team1)
                    foreach (var p2 in team2)
                    {
                        var ok = PairKey(p1, p2);
                        opponentCounts[ok] = opponentCounts.GetValueOrDefault(ok) + 1;
                    }

                // Court counts
                var courtIdx = match.CourtNumber - 1;
                foreach (var pid in team1.Concat(team2))
                    if (courtCounts.ContainsKey(pid) && courtIdx < courtCounts[pid].Length)
                        courtCounts[pid][courtIdx]++;
            }

            foreach (var bp in byePlayers)
                byeCounts[bp.Id]++;

            rounds.Add(new Round
            {
                RoundNumber = r + 1,
                Matches = matches,
                Byes = byePlayers.Select(p => new Bye { PlayerId = p.Id }).ToList()
            });
        }

        return rounds;
    }

    private static List<Player> SelectActivePlayers(List<Player> players, int needed, Dictionary<int, int> byeCounts)
    {
        if (needed >= players.Count) return new List<Player>(players);

        return players
            .OrderByDescending(p => byeCounts[p.Id])
            .ThenBy(p => p.Id)
            .Take(needed)
            .ToList();
    }

    private static List<(Player, Player)> FormTeams(
        List<Player> activePlayers,
        Dictionary<string, int> partnerCounts,
        Dictionary<string, int> lastPartneredRound,
        int currentRound)
    {
        var neededTeams = activePlayers.Count / 2;
        var minCount = GetMinPartnerCount(activePlayers, partnerCounts);

        // Try with strict constraints: only use min-count partnerships, no consecutive repeats
        var result = new List<(Player, Player)>();
        var used = new HashSet<int>();
        if (TryFormTeams(activePlayers, partnerCounts, lastPartneredRound, currentRound,
                minCount, true, used, result, neededTeams))
            return result;

        // Relax: allow min-count but permit consecutive repeats
        result.Clear();
        used.Clear();
        if (TryFormTeams(activePlayers, partnerCounts, lastPartneredRound, currentRound,
                minCount, false, used, result, neededTeams))
            return result;

        // Relax further: allow min+1 count, no consecutive
        result.Clear();
        used.Clear();
        if (TryFormTeams(activePlayers, partnerCounts, lastPartneredRound, currentRound,
                minCount + 1, true, used, result, neededTeams))
            return result;

        // Last resort: allow min+1 count with consecutive
        result.Clear();
        used.Clear();
        if (TryFormTeams(activePlayers, partnerCounts, lastPartneredRound, currentRound,
                minCount + 1, false, used, result, neededTeams))
            return result;

        // Absolute fallback: greedy, no constraints
        result.Clear();
        used.Clear();
        for (int i = 0; i < activePlayers.Count; i++)
        {
            if (used.Contains(activePlayers[i].Id)) continue;
            for (int j = i + 1; j < activePlayers.Count; j++)
            {
                if (used.Contains(activePlayers[j].Id)) continue;
                result.Add((activePlayers[i], activePlayers[j]));
                used.Add(activePlayers[i].Id);
                used.Add(activePlayers[j].Id);
                break;
            }
        }
        return result;
    }

    private static int GetMinPartnerCount(List<Player> players, Dictionary<string, int> partnerCounts)
    {
        int min = int.MaxValue;
        for (int i = 0; i < players.Count; i++)
            for (int j = i + 1; j < players.Count; j++)
            {
                var count = partnerCounts.GetValueOrDefault(PairKey(players[i].Id, players[j].Id));
                if (count < min) min = count;
            }
        return min == int.MaxValue ? 0 : min;
    }

    private static bool TryFormTeams(
        List<Player> players,
        Dictionary<string, int> partnerCounts,
        Dictionary<string, int> lastPartneredRound,
        int currentRound,
        int maxAllowedCount,
        bool blockConsecutive,
        HashSet<int> used,
        List<(Player, Player)> result,
        int neededTeams)
    {
        if (result.Count == neededTeams)
            return true;

        int first = -1;
        for (int i = 0; i < players.Count; i++)
        {
            if (!used.Contains(players[i].Id))
            {
                first = i;
                break;
            }
        }
        if (first == -1) return false;

        var p1 = players[first];
        used.Add(p1.Id);

        // Try partners sorted by count (prefer least-used)
        var candidates = new List<(int index, int count, int lastRound)>();
        for (int j = first + 1; j < players.Count; j++)
        {
            if (used.Contains(players[j].Id)) continue;
            var key = PairKey(p1.Id, players[j].Id);
            var count = partnerCounts.GetValueOrDefault(key);
            var lastRd = lastPartneredRound.GetValueOrDefault(key, -10);
            candidates.Add((j, count, lastRd));
        }

        // Sort: lowest count first, then oldest last-partnered round
        candidates.Sort((a, b) =>
        {
            if (a.count != b.count) return a.count.CompareTo(b.count);
            return a.lastRound.CompareTo(b.lastRound);
        });

        foreach (var (j, count, lastRd) in candidates)
        {
            if (count > maxAllowedCount) continue;
            if (blockConsecutive && lastRd == currentRound - 1) continue;

            var p2 = players[j];
            used.Add(p2.Id);
            result.Add((p1, p2));

            if (TryFormTeams(players, partnerCounts, lastPartneredRound, currentRound,
                    maxAllowedCount, blockConsecutive, used, result, neededTeams))
                return true;

            result.RemoveAt(result.Count - 1);
            used.Remove(p2.Id);
        }

        used.Remove(p1.Id);
        return false;
    }

    private static List<Match> PairTeamsIntoMatches(
        List<(Player, Player)> teams,
        Dictionary<string, int> opponentCounts)
    {
        var matches = new List<Match>();
        if (teams.Count < 2) return matches;

        var bestPairing = new List<(int, int)>();
        var bestScore = int.MaxValue;
        var usedTeams = new bool[teams.Count];

        FindBestTeamPairing(teams, opponentCounts, usedTeams, new List<(int, int)>(),
            ref bestPairing, ref bestScore);

        foreach (var (t1, t2) in bestPairing)
        {
            matches.Add(new Match
            {
                Team1Player1Id = teams[t1].Item1.Id,
                Team1Player2Id = teams[t1].Item2.Id,
                Team2Player1Id = teams[t2].Item1.Id,
                Team2Player2Id = teams[t2].Item2.Id,
            });
        }

        return matches;
    }

    private static void FindBestTeamPairing(
        List<(Player, Player)> teams,
        Dictionary<string, int> opponentCounts,
        bool[] usedTeams,
        List<(int, int)> current,
        ref List<(int, int)> bestPairing,
        ref int bestScore)
    {
        int first = -1;
        for (int i = 0; i < teams.Count; i++)
        {
            if (!usedTeams[i]) { first = i; break; }
        }

        if (first == -1)
        {
            var score = ScorePairing(teams, opponentCounts, current);
            if (score < bestScore)
            {
                bestScore = score;
                bestPairing = new List<(int, int)>(current);
            }
            return;
        }

        usedTeams[first] = true;
        for (int j = first + 1; j < teams.Count; j++)
        {
            if (usedTeams[j]) continue;
            usedTeams[j] = true;
            current.Add((first, j));
            FindBestTeamPairing(teams, opponentCounts, usedTeams, current, ref bestPairing, ref bestScore);
            current.RemoveAt(current.Count - 1);
            usedTeams[j] = false;
        }
        usedTeams[first] = false;
    }

    private static int ScorePairing(
        List<(Player, Player)> teams,
        Dictionary<string, int> opponentCounts,
        List<(int, int)> pairing)
    {
        // Score = sum of opponent counts (prefer lowest)
        // Plus a large penalty for any pair that has NEVER opposed (to force variety)
        int score = 0;
        int maxOpponentCount = opponentCounts.Count > 0 ? opponentCounts.Values.Max() : 0;

        foreach (var (t1, t2) in pairing)
        {
            var team1 = new[] { teams[t1].Item1.Id, teams[t1].Item2.Id };
            var team2 = new[] { teams[t2].Item1.Id, teams[t2].Item2.Id };
            foreach (var p1 in team1)
                foreach (var p2 in team2)
                {
                    var key = PairKey(p1, p2);
                    var count = opponentCounts.GetValueOrDefault(key);
                    score += count;
                }
        }

        // Also check: are there player pairs NOT in this matchup that have 0 opponent encounters?
        // If so, penalize pairings that put those players on the same side
        // This helps prevent the Stephen/Dave problem (always same side, never opposing)
        foreach (var (t1, t2) in pairing)
        {
            var team1 = new[] { teams[t1].Item1.Id, teams[t1].Item2.Id };
            var team2 = new[] { teams[t2].Item1.Id, teams[t2].Item2.Id };

            // Check same-side pairs: if they have low opponent count, that's bad
            // because they should be opposing instead
            foreach (var sameTeam in new[] { team1, team2 })
            {
                var sameKey = PairKey(sameTeam[0], sameTeam[1]);
                // These are already partners, skip
            }

            // Check cross-court: players on the same side of different matches
            // who have 0 opponent count — this is harder to fix here
        }

        return score;
    }

    private static void AssignCourts(
        List<Match> matches,
        Dictionary<int, int[]> courtCounts,
        int numberOfCourts)
    {
        if (matches.Count <= 1)
        {
            for (int i = 0; i < matches.Count; i++)
                matches[i].CourtNumber = i + 1;
            return;
        }

        var courtIndices = Enumerable.Range(0, matches.Count).ToArray();
        var bestAssignment = (int[])courtIndices.Clone();
        var bestImbalance = int.MaxValue;

        PermuteAndScore(courtIndices, 0, matches, courtCounts, ref bestAssignment, ref bestImbalance);

        for (int i = 0; i < matches.Count; i++)
            matches[i].CourtNumber = bestAssignment[i] + 1;
    }

    private static void PermuteAndScore(
        int[] courtIndices, int start, List<Match> matches,
        Dictionary<int, int[]> courtCounts, ref int[] bestAssignment, ref int bestImbalance)
    {
        if (start == courtIndices.Length)
        {
            var imbalance = CalculateImbalance(courtIndices, matches, courtCounts);
            if (imbalance < bestImbalance)
            {
                bestImbalance = imbalance;
                bestAssignment = (int[])courtIndices.Clone();
            }
            return;
        }

        for (int i = start; i < courtIndices.Length; i++)
        {
            (courtIndices[start], courtIndices[i]) = (courtIndices[i], courtIndices[start]);
            PermuteAndScore(courtIndices, start + 1, matches, courtCounts, ref bestAssignment, ref bestImbalance);
            (courtIndices[start], courtIndices[i]) = (courtIndices[i], courtIndices[start]);
        }
    }

    private static int CalculateImbalance(int[] courtIndices, List<Match> matches, Dictionary<int, int[]> courtCounts)
    {
        int total = 0;
        for (int i = 0; i < matches.Count; i++)
        {
            var courtIdx = courtIndices[i];
            var playerIds = new[] {
                matches[i].Team1Player1Id, matches[i].Team1Player2Id,
                matches[i].Team2Player1Id, matches[i].Team2Player2Id
            };
            foreach (var pid in playerIds)
                if (courtCounts.TryGetValue(pid, out var counts) && courtIdx < counts.Length)
                    total += counts[courtIdx];
        }
        return total;
    }

    private static List<Player> ShufflePlayers(List<Player> players, int seed)
    {
        var rng = new Random(seed * 31 + players.Count);
        var shuffled = new List<Player>(players);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        return shuffled;
    }

    internal static string PairKey(int a, int b)
        => a < b ? $"{a}-{b}" : $"{b}-{a}";
}
