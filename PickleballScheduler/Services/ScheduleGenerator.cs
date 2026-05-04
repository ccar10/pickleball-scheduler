using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public class ScheduleGenerator
{
    public ScheduleResult Generate(List<Player> players, int numberOfCourts, int numberOfRounds)
    {
        if (CanonicalSchedules.IsCanonical(players.Count, numberOfCourts))
            return new ScheduleResult(GenerateFromTable(players, numberOfCourts, numberOfRounds));

        return new ScheduleResult(GreedyScheduler.Generate(players, numberOfCourts, numberOfRounds));
    }

    // Canonical tables are 30 rounds long; rounds > RoundCount is out-of-scope per spec
    // (UI is expected to clamp). courts is unused here because IsCanonical already validated
    // players.Count / 4 == courts before we got here; canonical configs have no byes.
    private static List<Round> GenerateFromTable(List<Player> players, int courts, int rounds)
    {
        var output = new List<Round>(Math.Min(rounds, CanonicalSchedules.RoundCount));
        int max = Math.Min(rounds, CanonicalSchedules.RoundCount);
        for (int r = 0; r < max; r++)
        {
            output.Add(new Round
            {
                RoundNumber = r + 1,
                Matches = CanonicalSchedules.GetRound(players.Count, r, players),
                Byes = new List<Bye>(),
            });
        }
        return output;
    }
}
