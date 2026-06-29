using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public record StandingsRow(Player Player, int Wins, int PointsFor, int PointsAgainst)
{
    public int Diff => PointsFor - PointsAgainst;
}

public static class StandingsCalculator
{
    public static List<StandingsRow> Compute(IEnumerable<Player> players, IEnumerable<Round> rounds)
    {
        var wins = new Dictionary<int, int>();
        var pointsFor = new Dictionary<int, int>();
        var pointsAgainst = new Dictionary<int, int>();

        var playerList = players.ToList();
        foreach (var p in playerList)
        {
            wins[p.Id] = 0;
            pointsFor[p.Id] = 0;
            pointsAgainst[p.Id] = 0;
        }

        foreach (var round in rounds)
        {
            if (round.IsChampionship) continue;
            foreach (var m in round.Matches)
            {
                if (m.Team1Score is not int s1 || m.Team2Score is not int s2) continue;

                var team1 = new[] { m.Team1Player1Id, m.Team1Player2Id };
                var team2 = new[] { m.Team2Player1Id, m.Team2Player2Id };

                Accumulate(team1, s1, s2, s1 > s2);
                Accumulate(team2, s2, s1, s2 > s1);
            }
        }

        return playerList
            .Select(p => new StandingsRow(p, wins[p.Id], pointsFor[p.Id], pointsAgainst[p.Id]))
            .OrderByDescending(r => r.Wins)
            .ThenByDescending(r => r.Diff)
            .ThenByDescending(r => r.PointsFor)
            .ThenBy(r => r.Player.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Player.Id)
            .ToList();

        void Accumulate(int[] team, int scored, int conceded, bool won)
        {
            foreach (var id in team)
            {
                if (!wins.ContainsKey(id)) continue;
                if (won) wins[id]++;
                pointsFor[id] += scored;
                pointsAgainst[id] += conceded;
            }
        }
    }
}
