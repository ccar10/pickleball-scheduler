using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public class StandingsService
{
    public static List<PlayerStanding> Calculate(List<Player> players, List<Match> matches)
    {
        var stats = players.ToDictionary(p => p.Id, p => new PlayerStanding { Player = p });

        foreach (var match in matches.Where(m => m.IsComplete && m.Team1Score.HasValue && m.Team2Score.HasValue))
        {
            var t1Score = match.Team1Score!.Value;
            var t2Score = match.Team2Score!.Value;
            var diff = t1Score - t2Score;

            var team1Ids = new[] { match.Team1Player1Id, match.Team1Player2Id };
            var team2Ids = new[] { match.Team2Player1Id, match.Team2Player2Id };

            bool team1Won = t1Score > t2Score;

            foreach (var pid in team1Ids)
            {
                if (!stats.ContainsKey(pid)) continue;
                stats[pid].PointDifferential += diff;
                if (team1Won) stats[pid].Wins++; else stats[pid].Losses++;
            }
            foreach (var pid in team2Ids)
            {
                if (!stats.ContainsKey(pid)) continue;
                stats[pid].PointDifferential -= diff;
                if (!team1Won) stats[pid].Wins++; else stats[pid].Losses++;
            }
        }

        return stats.Values
            .OrderByDescending(s => s.Wins)
            .ThenByDescending(s => s.PointDifferential)
            .ToList();
    }
}

public class PlayerStanding
{
    public Player Player { get; set; } = null!;
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int PointDifferential { get; set; }
}
