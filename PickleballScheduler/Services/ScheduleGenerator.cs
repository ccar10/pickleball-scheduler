using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public class ScheduleGenerator
{
    public ScheduleResult Generate(List<Player> players, int numberOfCourts, int numberOfRounds)
    {
        var matchesPerRound = Math.Min(numberOfCourts, players.Count / 4);
        var playersPerRound = matchesPerRound * 4;
        var partnerCounts = new Dictionary<string, int>();
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

            RoundCandidate roundResult;
            if (circleSchedule != null && r < circleRounds)
            {
                // Circle method fixes partners; still run the joint search over opponent splits for that fixed partner set.
                var fixedTeams = circleSchedule[r];
                roundResult = BuildRoundFromFixedTeams(fixedTeams, partnerCounts, opponentCounts,
                    lastOpponentRound, courtCounts, r, matchesPerRound);
            }
            else
            {
                roundResult = BuildRound(activePlayers, players, partnerCounts, opponentCounts,
                    lastOpponentRound, courtCounts, r, matchesPerRound);
            }

            var matches = roundResult.Matches;

            // HR1: forced repeats — pair partner count was strictly greater than the
            // minimum partner count among pairs sharing a player with this pair, BEFORE this round.
            foreach (var match in matches)
            {
                foreach (var pair in new[] {
                    (match.Team1Player1Id, match.Team1Player2Id),
                    (match.Team2Player1Id, match.Team2Player2Id) })
                {
                    if (IsHr1Violation(pair.Item1, pair.Item2, players, partnerCounts))
                        hr1Violations++;
                }
            }

            // HR2: opponent pair faced in the immediately previous round.
            foreach (var match in matches)
            {
                var team1 = new[] { match.Team1Player1Id, match.Team1Player2Id };
                var team2 = new[] { match.Team2Player1Id, match.Team2Player2Id };
                foreach (var p1 in team1)
                    foreach (var p2 in team2)
                        if (IsHr2Violation(p1, p2, lastOpponentRound, r))
                            hr2Violations++;
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

    private record RoundCandidate(List<Match> Matches, CostTuple Cost);

    private static RoundCandidate BuildRound(
        List<Player> activePlayers,
        List<Player> allPlayers,
        Dictionary<string, int> partnerCounts,
        Dictionary<string, int> opponentCounts,
        Dictionary<string, int> lastOpponentRound,
        Dictionary<int, int[]> courtCounts,
        int currentRound,
        int numberOfCourts)
    {
        var best = new RoundCandidate(new List<Match>(), CostTuple.Worst);
        var used = new bool[activePlayers.Count];
        var current = new List<Match>(activePlayers.Count / 4);

        SearchMatches(activePlayers, allPlayers, partnerCounts, opponentCounts,
            lastOpponentRound, courtCounts, currentRound, numberOfCourts,
            used, current,
            runningHr1: 0, runningHr2: 0, runningPartnerSq: 0, runningOpponentSq: 0,
            ref best);

        // Court assignment is a separate pass — court contributes only level 5,
        // and the matchups themselves are independent of court number.
        AssignCourts(best.Matches, courtCounts, numberOfCourts);

        return best;
    }

    private static void SearchMatches(
        List<Player> active,
        List<Player> allPlayers,
        Dictionary<string, int> partnerCounts,
        Dictionary<string, int> opponentCounts,
        Dictionary<string, int> lastOpponentRound,
        Dictionary<int, int[]> courtCounts,
        int currentRound,
        int numberOfCourts,
        bool[] used,
        List<Match> current,
        int runningHr1,
        int runningHr2,
        long runningPartnerSq,
        long runningOpponentSq,
        ref RoundCandidate best)
    {
        // Compute partial cost. PartnerSqSum and OpponentSqSum are partial sums of squared post-update counts;
        // since each new match strictly increases them, the partial tuple is a valid lower bound.
        var partial = new CostTuple(
            runningHr1, runningHr2, runningPartnerSq, runningOpponentSq, CourtImbalance: 0);
        if (best.Cost.IsLessOrEqualTo(partial)) return;

        // Find first unused active player.
        int first = -1;
        for (int i = 0; i < active.Count; i++)
            if (!used[i]) { first = i; break; }

        if (first == -1)
        {
            // Round complete.
            var finalCost = new CostTuple(
                runningHr1, runningHr2, runningPartnerSq, runningOpponentSq, CourtImbalance: 0);
            if (finalCost.IsLessThan(best.Cost))
            {
                best = new RoundCandidate(new List<Match>(current.Select(m => new Match
                {
                    Team1Player1Id = m.Team1Player1Id,
                    Team1Player2Id = m.Team1Player2Id,
                    Team2Player1Id = m.Team2Player1Id,
                    Team2Player2Id = m.Team2Player2Id,
                    CourtNumber = m.CourtNumber,
                })), finalCost);
            }
            return;
        }

        // Pick a partner for `active[first]`.
        used[first] = true;
        var p1 = active[first];

        for (int j = first + 1; j < active.Count; j++)
        {
            if (used[j]) continue;
            var p1p2 = active[j];
            var partnerKey = PairKey(p1.Id, p1p2.Id);
            var partnerHr1 = IsHr1Violation(p1.Id, p1p2.Id, allPlayers, partnerCounts) ? 1 : 0;
            var partnerNewCount = partnerCounts.GetValueOrDefault(partnerKey) + 1;
            var partnerSqDelta = (long)partnerNewCount * partnerNewCount
                               - (long)(partnerNewCount - 1) * (partnerNewCount - 1);

            used[j] = true;

            // Pick the next anchor (lowest unused), then its partner — together with (first, j) forms one match.
            int anchor2 = -1;
            for (int k = first + 1; k < active.Count; k++)
                if (!used[k]) { anchor2 = k; break; }

            if (anchor2 == -1)
            {
                // Odd number — should not happen for active = 4k. Skip.
                used[j] = false;
                continue;
            }

            var p2 = active[anchor2];
            used[anchor2] = true;
            for (int m = anchor2 + 1; m < active.Count; m++)
            {
                if (used[m]) continue;
                var p2p2 = active[m];
                var partner2Key = PairKey(p2.Id, p2p2.Id);
                var partner2Hr1 = IsHr1Violation(p2.Id, p2p2.Id, allPlayers, partnerCounts) ? 1 : 0;
                var partner2NewCount = partnerCounts.GetValueOrDefault(partner2Key) + 1;
                var partner2SqDelta = (long)partner2NewCount * partner2NewCount
                                    - (long)(partner2NewCount - 1) * (partner2NewCount - 1);

                // Compute opponent contributions for the 4 cross-pairs.
                int matchHr2 = 0;
                long matchOppSq = 0;
                int[] team1 = { p1.Id, p1p2.Id };
                int[] team2 = { p2.Id, p2p2.Id };
                foreach (var x in team1)
                    foreach (var y in team2)
                    {
                        var ok = PairKey(x, y);
                        if (IsHr2Violation(x, y, lastOpponentRound, currentRound)) matchHr2++;
                        var oNew = opponentCounts.GetValueOrDefault(ok) + 1;
                        matchOppSq += (long)oNew * oNew - (long)(oNew - 1) * (oNew - 1);
                    }

                used[m] = true;
                current.Add(new Match
                {
                    Team1Player1Id = p1.Id,
                    Team1Player2Id = p1p2.Id,
                    Team2Player1Id = p2.Id,
                    Team2Player2Id = p2p2.Id,
                });

                // Mutate the running counts for recursion.
                partnerCounts[partnerKey] = partnerNewCount;
                partnerCounts[partner2Key] = partner2NewCount;
                foreach (var x in team1)
                    foreach (var y in team2)
                    {
                        var ok = PairKey(x, y);
                        opponentCounts[ok] = opponentCounts.GetValueOrDefault(ok) + 1;
                    }

                SearchMatches(active, allPlayers, partnerCounts, opponentCounts,
                    lastOpponentRound, courtCounts, currentRound, numberOfCourts,
                    used, current,
                    runningHr1 + partnerHr1 + partner2Hr1,
                    runningHr2 + matchHr2,
                    runningPartnerSq + partnerSqDelta + partner2SqDelta,
                    runningOpponentSq + matchOppSq,
                    ref best);

                // Undo.
                foreach (var x in team1)
                    foreach (var y in team2)
                    {
                        var ok = PairKey(x, y);
                        opponentCounts[ok]--;
                        if (opponentCounts[ok] == 0) opponentCounts.Remove(ok);
                    }
                partnerCounts[partnerKey] = partnerNewCount - 1;
                if (partnerCounts[partnerKey] == 0) partnerCounts.Remove(partnerKey);
                partnerCounts[partner2Key] = partner2NewCount - 1;
                if (partnerCounts[partner2Key] == 0) partnerCounts.Remove(partner2Key);

                current.RemoveAt(current.Count - 1);
                used[m] = false;
            }
            used[anchor2] = false;
            used[j] = false;
        }
        used[first] = false;
    }

    private static RoundCandidate BuildRoundFromFixedTeams(
        List<(Player, Player)> fixedTeams,
        Dictionary<string, int> partnerCounts,
        Dictionary<string, int> opponentCounts,
        Dictionary<string, int> lastOpponentRound,
        Dictionary<int, int[]> courtCounts,
        int currentRound,
        int numberOfCourts)
    {
        // Partners are fixed by the circle method; just enumerate ways to pair teams into matches.
        var best = new RoundCandidate(new List<Match>(), CostTuple.Worst);
        var used = new bool[fixedTeams.Count];
        var current = new List<Match>(fixedTeams.Count / 2);

        SearchTeamPairings(fixedTeams, opponentCounts, lastOpponentRound, currentRound,
            used, current, runningHr2: 0, runningOpponentSq: 0, ref best);

        // Partner cost is fixed (no choice), but we still need to roll partner sums into the cost
        // for accurate lex compare across rounds — for circle rounds it's irrelevant since we don't
        // compare circle vs non-circle within the same round.

        AssignCourts(best.Matches, courtCounts, numberOfCourts);
        return best;
    }

    private static void SearchTeamPairings(
        List<(Player, Player)> teams,
        Dictionary<string, int> opponentCounts,
        Dictionary<string, int> lastOpponentRound,
        int currentRound,
        bool[] used,
        List<Match> current,
        int runningHr2,
        long runningOpponentSq,
        ref RoundCandidate best)
    {
        var partial = new CostTuple(0, runningHr2, 0, runningOpponentSq, 0);
        if (best.Cost.IsLessOrEqualTo(partial)) return;

        int first = -1;
        for (int i = 0; i < teams.Count; i++)
            if (!used[i]) { first = i; break; }

        if (first == -1)
        {
            var finalCost = new CostTuple(0, runningHr2, 0, runningOpponentSq, 0);
            if (finalCost.IsLessThan(best.Cost))
            {
                best = new RoundCandidate(new List<Match>(current.Select(m => new Match
                {
                    Team1Player1Id = m.Team1Player1Id,
                    Team1Player2Id = m.Team1Player2Id,
                    Team2Player1Id = m.Team2Player1Id,
                    Team2Player2Id = m.Team2Player2Id,
                })), finalCost);
            }
            return;
        }

        used[first] = true;
        var t1 = teams[first];
        for (int j = first + 1; j < teams.Count; j++)
        {
            if (used[j]) continue;
            var t2 = teams[j];
            int matchHr2 = 0;
            long matchOppSq = 0;
            int[] team1 = { t1.Item1.Id, t1.Item2.Id };
            int[] team2 = { t2.Item1.Id, t2.Item2.Id };
            foreach (var x in team1)
                foreach (var y in team2)
                {
                    var ok = PairKey(x, y);
                    if (IsHr2Violation(x, y, lastOpponentRound, currentRound)) matchHr2++;
                    var oNew = opponentCounts.GetValueOrDefault(ok) + 1;
                    matchOppSq += (long)oNew * oNew - (long)(oNew - 1) * (oNew - 1);
                }

            used[j] = true;
            current.Add(new Match
            {
                Team1Player1Id = t1.Item1.Id,
                Team1Player2Id = t1.Item2.Id,
                Team2Player1Id = t2.Item1.Id,
                Team2Player2Id = t2.Item2.Id,
            });
            foreach (var x in team1)
                foreach (var y in team2)
                {
                    var ok = PairKey(x, y);
                    opponentCounts[ok] = opponentCounts.GetValueOrDefault(ok) + 1;
                }

            SearchTeamPairings(teams, opponentCounts, lastOpponentRound, currentRound,
                used, current, runningHr2 + matchHr2, runningOpponentSq + matchOppSq, ref best);

            foreach (var x in team1)
                foreach (var y in team2)
                {
                    var ok = PairKey(x, y);
                    opponentCounts[ok]--;
                    if (opponentCounts[ok] == 0) opponentCounts.Remove(ok);
                }
            current.RemoveAt(current.Count - 1);
            used[j] = false;
        }
        used[first] = false;
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

    public static string PairKey(int a, int b)
        => a < b ? $"{a}-{b}" : $"{b}-{a}";

    public static bool IsHr1Violation(
        int a, int b, List<Player> players, Dictionary<string, int> partnerCounts)
    {
        var key = PairKey(a, b);
        var thisCount = partnerCounts.GetValueOrDefault(key);
        if (thisCount == 0) return false;

        int minSibling = int.MaxValue;
        foreach (var p in players)
        {
            if (p.Id == a || p.Id == b) continue;
            var ka = PairKey(a, p.Id);
            var kb = PairKey(b, p.Id);
            var ca = partnerCounts.GetValueOrDefault(ka);
            var cb = partnerCounts.GetValueOrDefault(kb);
            if (ca < minSibling) minSibling = ca;
            if (cb < minSibling) minSibling = cb;
        }
        if (minSibling == int.MaxValue) minSibling = 0;
        return thisCount > minSibling;
    }

    public static bool IsHr2Violation(
        int a, int b, Dictionary<string, int> lastOpponentRound, int currentRound)
    {
        var key = PairKey(a, b);
        return lastOpponentRound.GetValueOrDefault(key, -10) == currentRound - 1;
    }

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

}
