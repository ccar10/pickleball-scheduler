using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public class ScheduleGenerator
{
    private const int BeamWidthLargeActive = 1;
    private const int BeamThreshold = 9;

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

        int hr1Violations = 0;
        int hr2Violations = 0;
        var lastOpponentRound = new Dictionary<string, int>();

        for (int r = 0; r < numberOfRounds; r++)
        {
            var activePlayers = SelectActivePlayers(players, playersPerRound, byeCounts);
            var byePlayers = players.Where(p => !activePlayers.Contains(p)).ToList();

            List<Match> matches;
            bool useWhist =
                WhistCyclicSchedule.IsSupportedSize(players.Count) &&
                numberOfCourts >= players.Count / 4 &&
                numberOfRounds >= players.Count - 1 &&
                r < players.Count - 1;

            if (useWhist)
            {
                matches = WhistCyclicSchedule.GetRoundMatchups(players, r);
                AssignCourts(matches, courtCounts, matchesPerRound);
            }
            else
            {
                var roundResult = BuildRound(activePlayers, players, partnerCounts, opponentCounts,
                    lastOpponentRound, courtCounts, r, matchesPerRound);
                matches = roundResult.Matches;
            }

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

        // Collect partner candidates with their incremental cost.
        var candidates = new List<(int j, long incCost)>();
        for (int j = first + 1; j < active.Count; j++)
        {
            if (used[j]) continue;
            var cp1p2 = active[j];
            var cPartnerKey = PairKey(p1.Id, cp1p2.Id);
            var cPartnerHr1 = IsHr1Violation(p1.Id, cp1p2.Id, allPlayers, partnerCounts) ? 1 : 0;
            var cPartnerNewCount = partnerCounts.GetValueOrDefault(cPartnerKey) + 1;
            var cPartnerSqDelta = (long)cPartnerNewCount * cPartnerNewCount
                                - (long)(cPartnerNewCount - 1) * (cPartnerNewCount - 1);
            var incCost = (long)cPartnerHr1 * 1_000_000L + cPartnerSqDelta;
            candidates.Add((j, incCost));
        }
        candidates.Sort((a, b) => a.incCost.CompareTo(b.incCost));

        int limit = active.Count > BeamThreshold ? Math.Min(BeamWidthLargeActive, candidates.Count) : candidates.Count;
        for (int idx = 0; idx < limit; idx++)
        {
            var (j, _) = candidates[idx];
            var p1p2 = active[j];
            var partnerKey = PairKey(p1.Id, p1p2.Id);
            var partnerHr1 = IsHr1Violation(p1.Id, p1p2.Id, allPlayers, partnerCounts) ? 1 : 0;
            var partnerNewCount = partnerCounts.GetValueOrDefault(partnerKey) + 1;
            var partnerSqDelta = (long)partnerNewCount * partnerNewCount
                               - (long)(partnerNewCount - 1) * (partnerNewCount - 1);

            used[j] = true;

            // Match 1's other team is any 2-subset of the remaining unused players.
            // Enumerate all (k, m) with k < m, both unused, both != first and != j.
            for (int k = 0; k < active.Count; k++)
            {
                if (used[k]) continue;
                for (int m = k + 1; m < active.Count; m++)
                {
                    if (used[m]) continue;
                    var p2 = active[k];
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

                    used[k] = true;
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
                    used[k] = false;
                    used[m] = false;
                }
            }
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
        var bestSpread = int.MaxValue;

        PermuteAndScore(courtIndices, 0, matches, courtCounts,
            ref bestAssignment, ref bestImbalance, ref bestSpread);

        for (int i = 0; i < matches.Count; i++)
            matches[i].CourtNumber = bestAssignment[i] + 1;
    }

    private static void PermuteAndScore(
        int[] courtIndices, int start, List<Match> matches,
        Dictionary<int, int[]> courtCounts,
        ref int[] bestAssignment, ref int bestImbalance, ref int bestSpread)
    {
        if (start == courtIndices.Length)
        {
            var (imbalance, spread) = CalculateImbalance(courtIndices, matches, courtCounts);
            if (imbalance < bestImbalance ||
                (imbalance == bestImbalance && spread < bestSpread))
            {
                bestImbalance = imbalance;
                bestSpread = spread;
                bestAssignment = (int[])courtIndices.Clone();
            }
            return;
        }

        for (int i = start; i < courtIndices.Length; i++)
        {
            (courtIndices[start], courtIndices[i]) = (courtIndices[i], courtIndices[start]);
            PermuteAndScore(courtIndices, start + 1, matches, courtCounts,
                ref bestAssignment, ref bestImbalance, ref bestSpread);
            (courtIndices[start], courtIndices[i]) = (courtIndices[i], courtIndices[start]);
        }
    }

    /// <summary>
    /// Returns (totalImbalance, spreadAfter) for an assignment.
    /// totalImbalance = sum over playing players of their prior count on the court they're being put on.
    /// spreadAfter = sum over playing players of (max - min) of their per-court counts after this round
    /// would be applied. Used as a tie-breaker so a player stuck on one court gets variety even when
    /// totals are equal.
    /// </summary>
    private static (int total, int spread) CalculateImbalance(
        int[] courtIndices, List<Match> matches, Dictionary<int, int[]> courtCounts)
    {
        int total = 0;
        int spread = 0;
        for (int i = 0; i < matches.Count; i++)
        {
            var courtIdx = courtIndices[i];
            var playerIds = new[] {
                matches[i].Team1Player1Id, matches[i].Team1Player2Id,
                matches[i].Team2Player1Id, matches[i].Team2Player2Id
            };
            foreach (var pid in playerIds)
            {
                if (!courtCounts.TryGetValue(pid, out var counts) || courtIdx >= counts.Length) continue;
                total += counts[courtIdx];

                int max = 0, min = int.MaxValue;
                for (int c = 0; c < counts.Length; c++)
                {
                    int v = counts[c] + (c == courtIdx ? 1 : 0);
                    if (v > max) max = v;
                    if (v < min) min = v;
                }
                spread += max - min;
            }
        }
        return (total, spread);
    }

    public static string PairKey(int a, int b)
        => a < b ? $"{a}-{b}" : $"{b}-{a}";

    /// <summary>
    /// Try near-neighbor configurations (player count and round count adjusted by ±1, ±2, ±3)
    /// and return a human-readable suggestion for the smallest change that yields zero violations,
    /// or null if no near-neighbor works.
    /// </summary>
    public static string? TrySuggestZeroViolationConfig(
        List<Player> players, int courts, int rounds)
    {
        var generator = new ScheduleGenerator();

        // If the current config is already zero-violation, no suggestion is needed.
        try
        {
            var currentResult = generator.Generate(players, courts, rounds);
            if (currentResult.Hr1Violations == 0 && currentResult.Hr2Violations == 0)
            {
                return null;
            }
        }
        catch { /* if current config throws, fall through to neighbor search */ }

        // Search neighbors ordered by total |delta|, smallest first.
        var deltas = new List<(int dPlayers, int dRounds)>();
        for (int dp = -3; dp <= 3; dp++)
            for (int dr = -3; dr <= 3; dr++)
            {
                if (dp == 0 && dr == 0) continue;
                deltas.Add((dp, dr));
            }
        deltas.Sort((a, b) => (Math.Abs(a.dPlayers) + Math.Abs(a.dRounds))
                             .CompareTo(Math.Abs(b.dPlayers) + Math.Abs(b.dRounds)));

        foreach (var (dp, dr) in deltas)
        {
            int candidatePlayers = players.Count + dp;
            int candidateRounds = rounds + dr;
            if (candidatePlayers < 4 || candidateRounds < 1) continue;

            var candidatePlayerList = Enumerable.Range(1, candidatePlayers)
                .Select(id => new Player { Id = id, Name = $"P{id}" })
                .ToList();

            try
            {
                var result = generator.Generate(candidatePlayerList, courts, candidateRounds);
                if (result.Hr1Violations == 0 && result.Hr2Violations == 0)
                {
                    return $"{candidatePlayers} players and {candidateRounds} rounds";
                }
            }
            catch { /* skip infeasible configs */ }
        }
        return null;
    }

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

}
