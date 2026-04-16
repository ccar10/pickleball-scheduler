using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public class ScheduleGenerator
{
    public List<Round> Generate(List<Player> players, int numberOfCourts, int numberOfRounds)
    {
        var matchesPerRound = Math.Min(numberOfCourts, players.Count / 4);
        var playersPerRound = matchesPerRound * 4;
        var usedPartnerships = new HashSet<string>();
        var usedOpponents = new Dictionary<string, int>();
        var courtCounts = new Dictionary<int, int[]>(); // playerId -> count per court index
        var byeCounts = players.ToDictionary(p => p.Id, _ => 0);
        var rounds = new List<Round>();

        // Initialize court counts
        foreach (var p in players)
        {
            courtCounts[p.Id] = new int[matchesPerRound];
        }

        for (int r = 0; r < numberOfRounds; r++)
        {
            var activePlayers = SelectActivePlayers(players, playersPerRound, byeCounts);
            var byePlayers = players.Where(p => !activePlayers.Contains(p)).ToList();

            // Shuffle active players to vary which players end up in which match/court
            // Uses a deterministic shuffle based on round number for reproducibility
            var shuffled = ShufflePlayers(activePlayers, r);

            // Form teams with backtracking (no repeated partners, prefer new opponents)
            var teams = FormTeams(shuffled, usedPartnerships);

            // If backtracking failed (all partnerships exhausted), reset and try again
            // This handles rounds beyond the partner limit (e.g., round 8+ with 8 players)
            if (teams.Count < matchesPerRound)
            {
                usedPartnerships.Clear();
                teams = FormTeams(shuffled, usedPartnerships);
            }

            // Pair teams into matches, preferring opponents who haven't faced each other
            var matches = PairTeamsIntoMatches(teams, usedOpponents);

            // Assign matches to courts, balancing court assignments
            AssignCourts(matches, courtCounts, matchesPerRound);

            // Record partnerships
            foreach (var match in matches)
            {
                usedPartnerships.Add(PairKey(match.Team1Player1Id, match.Team1Player2Id));
                usedPartnerships.Add(PairKey(match.Team2Player1Id, match.Team2Player2Id));

                // Record opponent pairs
                var team1 = new[] { match.Team1Player1Id, match.Team1Player2Id };
                var team2 = new[] { match.Team2Player1Id, match.Team2Player2Id };
                foreach (var p1 in team1)
                {
                    foreach (var p2 in team2)
                    {
                        var key = PairKey(p1, p2);
                        usedOpponents[key] = usedOpponents.GetValueOrDefault(key) + 1;
                    }
                }

                // Record court assignments
                var courtIdx = match.CourtNumber - 1;
                foreach (var pid in team1.Concat(team2))
                {
                    if (courtCounts.ContainsKey(pid) && courtIdx < courtCounts[pid].Length)
                    {
                        courtCounts[pid][courtIdx]++;
                    }
                }
            }

            foreach (var bp in byePlayers)
            {
                byeCounts[bp.Id]++;
            }

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

    private static List<(Player, Player)> FormTeams(List<Player> activePlayers, HashSet<string> usedPartnerships)
    {
        var neededTeams = activePlayers.Count / 2;
        var result = new List<(Player, Player)>();
        var used = new HashSet<int>();

        if (TryFormTeams(activePlayers, usedPartnerships, used, result, neededTeams))
        {
            return result;
        }

        // Fallback: greedy pairing allowing repeats if backtracking fails
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

    private static bool TryFormTeams(
        List<Player> players,
        HashSet<string> usedPartnerships,
        HashSet<int> used,
        List<(Player, Player)> result,
        int neededTeams)
    {
        if (result.Count == neededTeams)
            return true;

        // Find the first unused player
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

        for (int j = first + 1; j < players.Count; j++)
        {
            if (used.Contains(players[j].Id)) continue;

            var key = PairKey(p1.Id, players[j].Id);
            if (usedPartnerships.Contains(key)) continue;

            var p2 = players[j];
            used.Add(p2.Id);
            result.Add((p1, p2));

            if (TryFormTeams(players, usedPartnerships, used, result, neededTeams))
                return true;

            result.RemoveAt(result.Count - 1);
            used.Remove(p2.Id);
        }

        used.Remove(p1.Id);
        return false;
    }

    private static List<Match> PairTeamsIntoMatches(
        List<(Player, Player)> teams,
        Dictionary<string, int> usedOpponents)
    {
        var matches = new List<Match>();

        if (teams.Count < 2)
            return matches;

        // Try all possible pairings of teams and pick the one with lowest opponent overlap
        var bestPairing = new List<(int, int)>();
        var bestScore = int.MaxValue;
        var usedTeams = new bool[teams.Count];

        FindBestTeamPairing(teams, usedOpponents, usedTeams, new List<(int, int)>(),
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
        Dictionary<string, int> usedOpponents,
        bool[] usedTeams,
        List<(int, int)> current,
        ref List<(int, int)> bestPairing,
        ref int bestScore)
    {
        // Find first unused team
        int first = -1;
        for (int i = 0; i < teams.Count; i++)
        {
            if (!usedTeams[i]) { first = i; break; }
        }

        if (first == -1)
        {
            // All teams paired - evaluate
            var score = ScorePairing(teams, usedOpponents, current);
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

            FindBestTeamPairing(teams, usedOpponents, usedTeams, current, ref bestPairing, ref bestScore);

            current.RemoveAt(current.Count - 1);
            usedTeams[j] = false;
        }
        usedTeams[first] = false;
    }

    private static int ScorePairing(
        List<(Player, Player)> teams,
        Dictionary<string, int> usedOpponents,
        List<(int, int)> pairing)
    {
        int score = 0;
        foreach (var (t1, t2) in pairing)
        {
            var team1Players = new[] { teams[t1].Item1.Id, teams[t1].Item2.Id };
            var team2Players = new[] { teams[t2].Item1.Id, teams[t2].Item2.Id };
            foreach (var p1 in team1Players)
            {
                foreach (var p2 in team2Players)
                {
                    var key = PairKey(p1, p2);
                    score += usedOpponents.GetValueOrDefault(key);
                }
            }
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
            {
                matches[i].CourtNumber = i + 1;
            }
            return;
        }

        // Try all permutations of court assignments and pick the most balanced
        var courtIndices = Enumerable.Range(0, matches.Count).ToArray();
        var bestAssignment = (int[])courtIndices.Clone();
        var bestImbalance = int.MaxValue;

        PermuteAndScore(courtIndices, 0, matches, courtCounts, ref bestAssignment, ref bestImbalance);

        for (int i = 0; i < matches.Count; i++)
        {
            matches[i].CourtNumber = bestAssignment[i] + 1;
        }
    }

    private static void PermuteAndScore(
        int[] courtIndices,
        int start,
        List<Match> matches,
        Dictionary<int, int[]> courtCounts,
        ref int[] bestAssignment,
        ref int bestImbalance)
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

    private static int CalculateImbalance(
        int[] courtIndices,
        List<Match> matches,
        Dictionary<int, int[]> courtCounts)
    {
        int total = 0;
        for (int i = 0; i < matches.Count; i++)
        {
            var courtIdx = courtIndices[i];
            var playerIds = new[]
            {
                matches[i].Team1Player1Id, matches[i].Team1Player2Id,
                matches[i].Team2Player1Id, matches[i].Team2Player2Id
            };
            foreach (var pid in playerIds)
            {
                if (courtCounts.TryGetValue(pid, out var counts) && courtIdx < counts.Length)
                {
                    total += counts[courtIdx];
                }
            }
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
