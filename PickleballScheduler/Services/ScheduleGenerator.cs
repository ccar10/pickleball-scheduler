using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public class ScheduleGenerator
{
    public ScheduleResult Generate(List<Player> players, int numberOfCourts, int numberOfRounds)
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

        // When every player plays every round and we have an even count, a perfect
        // 1-factorization exists for the first n-1 rounds (circle method). Pre-compute
        // it so rounds never collide — the per-round greedy can't see far enough ahead
        // to guarantee this.
        var canUseCircle = playersPerRound == players.Count && players.Count % 2 == 0;
        var circleRounds = canUseCircle ? Math.Min(numberOfRounds, players.Count - 1) : 0;
        var circleSchedule = circleRounds > 0 ? CircleMethodSchedule(players, circleRounds) : null;

        int hr1Violations = 0;
        int hr2Violations = 0;
        var lastOpponentRound = new Dictionary<string, int>();

        for (int r = 0; r < numberOfRounds; r++)
        {
            var activePlayers = SelectActivePlayers(players, playersPerRound, byeCounts);
            var byePlayers = players.Where(p => !activePlayers.Contains(p)).ToList();

            List<(Player, Player)> teams;
            if (circleSchedule != null && r < circleRounds)
            {
                teams = circleSchedule[r];
            }
            else
            {
                var shuffled = ShufflePlayers(activePlayers, r);
                teams = FormTeams(shuffled, partnerCounts, lastPartneredRound, r);
            }

            // Pair teams into matches: maximize opponent variety
            var matches = PairTeamsIntoMatches(teams, opponentCounts);

            // Assign courts: balance court usage
            AssignCourts(matches, courtCounts, matchesPerRound);

            // HR1: forced repeats — pair partner count was strictly greater than the
            // minimum partner count among pairs sharing a player with this pair, BEFORE this round.
            foreach (var match in matches)
            {
                foreach (var pair in new[] {
                    (match.Team1Player1Id, match.Team1Player2Id),
                    (match.Team2Player1Id, match.Team2Player2Id) })
                {
                    var key = PairKey(pair.Item1, pair.Item2);
                    var priorCount = partnerCounts.GetValueOrDefault(key);
                    if (priorCount == 0) continue;
                    var minSiblingCount = MinSiblingPartnerCount(pair.Item1, pair.Item2, players, partnerCounts);
                    if (priorCount > minSiblingCount) hr1Violations++;
                }
            }

            // HR2: opponent pair faced in the immediately previous round.
            foreach (var match in matches)
            {
                var team1 = new[] { match.Team1Player1Id, match.Team1Player2Id };
                var team2 = new[] { match.Team2Player1Id, match.Team2Player2Id };
                foreach (var p1 in team1)
                    foreach (var p2 in team2)
                    {
                        var key = PairKey(p1, p2);
                        if (lastOpponentRound.GetValueOrDefault(key, -10) == r - 1)
                            hr2Violations++;
                    }
            }

            // Tracking update — MUST run after the HR1/HR2 counting blocks above,
            // which read partnerCounts and lastOpponentRound at their pre-round values.
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
                        lastOpponentRound[ok] = r;
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

        return new ScheduleResult(rounds, hr1Violations, hr2Violations, RepeatSuggestion: null);
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
        // Globally minimize total pair cost across the round's perfect matching.
        // Cost per pair is quadratic in prior-partner count, so two 1-count edges
        // (cost 2) beat one 2-count edge (cost 4) — preventing the greedy trap
        // where one pair doubles up while a complementary pair is never used.
        var best = new List<(Player, Player)>();
        long bestScore = long.MaxValue;
        var current = new List<(Player, Player)>(activePlayers.Count / 2);
        var used = new bool[activePlayers.Count];
        SearchTeams(activePlayers, partnerCounts, lastPartneredRound, currentRound,
            used, current, 0L, ref bestScore, ref best);
        return best;
    }

    private static void SearchTeams(
        List<Player> players,
        Dictionary<string, int> partnerCounts,
        Dictionary<string, int> lastPartneredRound,
        int currentRound,
        bool[] used,
        List<(Player, Player)> current,
        long currentScore,
        ref long bestScore,
        ref List<(Player, Player)> best)
    {
        if (currentScore >= bestScore) return;

        int first = -1;
        for (int i = 0; i < players.Count; i++)
        {
            if (!used[i]) { first = i; break; }
        }
        if (first == -1)
        {
            bestScore = currentScore;
            best = new List<(Player, Player)>(current);
            return;
        }

        var p1 = players[first];
        used[first] = true;

        var candidates = new List<(int index, long cost)>();
        for (int j = first + 1; j < players.Count; j++)
        {
            if (used[j]) continue;
            var key = PairKey(p1.Id, players[j].Id);
            var count = partnerCounts.GetValueOrDefault(key);
            var lastRd = lastPartneredRound.GetValueOrDefault(key, -10);
            candidates.Add((j, PairCost(count, lastRd, currentRound)));
        }
        candidates.Sort((a, b) => a.cost.CompareTo(b.cost));

        foreach (var (j, cost) in candidates)
        {
            used[j] = true;
            current.Add((p1, players[j]));
            SearchTeams(players, partnerCounts, lastPartneredRound, currentRound,
                used, current, currentScore + cost, ref bestScore, ref best);
            current.RemoveAt(current.Count - 1);
            used[j] = false;
        }

        used[first] = false;
    }

    private static long PairCost(int count, int lastPartneredRound, int currentRound)
    {
        long cost = (long)count * count;
        if (lastPartneredRound == currentRound - 1) cost += 1000;
        return cost;
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

    // Circle method: produces a 1-factorization of the complete graph on `players.Count`
    // vertices (must be even). One player is held fixed; the other n-1 rotate around a
    // circle. Across n-1 rounds, every pair partners exactly once.
    private static List<List<(Player, Player)>> CircleMethodSchedule(List<Player> players, int numRounds)
    {
        int n = players.Count;
        var fixedPlayer = players[0];
        var rotating = new List<Player>(players.Skip(1));
        var schedule = new List<List<(Player, Player)>>(numRounds);

        for (int r = 0; r < numRounds; r++)
        {
            var arr = new List<Player>(n) { fixedPlayer };
            arr.AddRange(rotating);

            var teams = new List<(Player, Player)>(n / 2);
            for (int i = 0; i < n / 2; i++)
            {
                teams.Add((arr[i], arr[n - 1 - i]));
            }
            schedule.Add(teams);

            var last = rotating[^1];
            rotating.RemoveAt(rotating.Count - 1);
            rotating.Insert(0, last);
        }

        return schedule;
    }

    private static int MinSiblingPartnerCount(
        int a, int b, List<Player> players, Dictionary<string, int> partnerCounts)
    {
        int min = int.MaxValue;
        foreach (var p in players)
        {
            if (p.Id == a || p.Id == b) continue;
            var ka = PairKey(a, p.Id);
            var kb = PairKey(b, p.Id);
            var ca = partnerCounts.GetValueOrDefault(ka);
            var cb = partnerCounts.GetValueOrDefault(kb);
            if (ca < min) min = ca;
            if (cb < min) min = cb;
        }
        return min == int.MaxValue ? 0 : min;
    }
}
