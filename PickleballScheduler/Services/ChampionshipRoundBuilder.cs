using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public static class ChampionshipRoundBuilder
{
    public static Round? Build(IReadOnlyList<StandingsRow> standings, int numberOfCourts, int nextRoundNumber)
    {
        int capacity = Math.Min(numberOfCourts, standings.Count / 4);
        if (capacity == 0) return null;

        var round = new Round { RoundNumber = nextRoundNumber, IsChampionship = true };

        for (int court = 0; court < capacity; court++)
        {
            var s1 = standings[court * 4 + 0].Player;
            var s2 = standings[court * 4 + 1].Player;
            var s3 = standings[court * 4 + 2].Player;
            var s4 = standings[court * 4 + 3].Player;

            round.Matches.Add(new Match
            {
                CourtNumber = court + 1,
                Team1Player1Id = s1.Id, Team1Player2Id = s4.Id,
                Team2Player1Id = s2.Id, Team2Player2Id = s3.Id,
            });
        }

        for (int i = capacity * 4; i < standings.Count; i++)
            round.Byes.Add(new Bye { PlayerId = standings[i].Player.Id });

        return round;
    }
}
