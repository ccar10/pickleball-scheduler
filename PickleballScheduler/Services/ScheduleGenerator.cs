using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public class ScheduleGenerator
{
    public List<Round> Generate(List<Player> players, int numberOfCourts, int numberOfRounds, bool useSkillBalancing)
    {
        var matchesPerRound = Math.Min(numberOfCourts, players.Count / 4);
        var playersPerRound = matchesPerRound * 4;
        var usedPartnerships = new HashSet<string>();
        var byeCounts = players.ToDictionary(p => p.Id, _ => 0);
        var rounds = new List<Round>();

        for (int r = 0; r < numberOfRounds; r++)
        {
            var activePlayers = SelectActivePlayers(players, playersPerRound, byeCounts);
            var byePlayers = players.Where(p => !activePlayers.Contains(p)).ToList();

            List<(Player, Player)> teams;
            if (useSkillBalancing)
            {
                teams = FormSkillBalancedTeams(activePlayers, usedPartnerships);
            }
            else
            {
                teams = FormTeams(activePlayers, usedPartnerships);
            }

            var matches = new List<Match>();
            for (int i = 0; i + 1 < teams.Count; i += 2)
            {
                matches.Add(new Match
                {
                    CourtNumber = (i / 2) + 1,
                    Team1Player1Id = teams[i].Item1.Id,
                    Team1Player2Id = teams[i].Item2.Id,
                    Team2Player1Id = teams[i + 1].Item1.Id,
                    Team2Player2Id = teams[i + 1].Item2.Id,
                });
            }

            foreach (var team in teams)
            {
                usedPartnerships.Add(PairKey(team.Item1.Id, team.Item2.Id));
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

        if (TryFormTeams(activePlayers, usedPartnerships, 0, used, result, neededTeams))
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
        int startIndex,
        HashSet<int> used,
        List<(Player, Player)> result,
        int neededTeams)
    {
        if (result.Count == neededTeams)
            return true;

        // Find the first unused player
        int first = -1;
        for (int i = startIndex; i < players.Count; i++)
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

            if (TryFormTeams(players, usedPartnerships, first + 1, used, result, neededTeams))
                return true;

            result.RemoveAt(result.Count - 1);
            used.Remove(p2.Id);
        }

        used.Remove(p1.Id);
        return false;
    }

    private static List<(Player, Player)> FormSkillBalancedTeams(List<Player> activePlayers, HashSet<string> usedPartnerships)
    {
        var sorted = activePlayers
            .OrderByDescending(p => p.DuprRating ?? 3.0m)
            .ToList();

        var teams = new List<(Player, Player)>();
        var used = new HashSet<int>();

        int lo = 0, hi = sorted.Count - 1;
        while (lo < hi)
        {
            if (used.Contains(sorted[lo].Id)) { lo++; continue; }
            if (used.Contains(sorted[hi].Id)) { hi--; continue; }

            var key = PairKey(sorted[lo].Id, sorted[hi].Id);
            if (!usedPartnerships.Contains(key))
            {
                teams.Add((sorted[lo], sorted[hi]));
                used.Add(sorted[lo].Id);
                used.Add(sorted[hi].Id);
            }
            lo++;
            hi--;
        }

        // Fallback for remaining unpaired
        var unpaired = sorted.Where(p => !used.Contains(p.Id)).ToList();
        for (int i = 0; i + 1 < unpaired.Count; i += 2)
        {
            teams.Add((unpaired[i], unpaired[i + 1]));
        }

        return teams;
    }

    private static string PairKey(int a, int b)
        => a < b ? $"{a}-{b}" : $"{b}-{a}";
}
