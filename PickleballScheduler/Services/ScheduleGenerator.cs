using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public class ScheduleGenerator
{
    public ScheduleResult Generate(List<Player> players, int numberOfCourts, int numberOfRounds)
    {
        return new ScheduleResult(GreedyScheduler.Generate(players, numberOfCourts, numberOfRounds));
    }
}
