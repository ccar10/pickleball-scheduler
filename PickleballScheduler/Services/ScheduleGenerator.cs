using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public class ScheduleGenerator
{
    /// <summary>
    /// For canonical sizes (8/12/16/20/24) with sufficient courts, the first n-1 rounds use
    /// cyclic Whist matchups (every pair partners exactly once, every pair opposes exactly twice).
    /// Round n and beyond fall through to greedy matchup selection. Court labels for every round
    /// are assigned by per-round permutation against the running per-player court counts.
    /// All other configurations use greedy throughout.
    /// </summary>
    public ScheduleResult Generate(List<Player> players, int numberOfCourts, int numberOfRounds)
    {
        var matchesPerRound = Math.Min(numberOfCourts, players.Count / 4);
        var partnerCount = new Dictionary<string, int>();
        var opponentCount = new Dictionary<string, int>();
        var courtCount = players.ToDictionary(p => p.Id, _ => new int[matchesPerRound]);
        var byeCount = players.ToDictionary(p => p.Id, _ => 0);
        var lastByeRound = new Dictionary<int, int>();

        bool useWhist = WhistMatchups.IsSupportedSize(players.Count)
                        && numberOfCourts >= players.Count / 4;

        var output = new List<Round>(numberOfRounds);

        for (int r = 0; r < numberOfRounds; r++)
        {
            List<Match> matches;
            List<Player> byes;

            bool whistRound = useWhist && r < players.Count - 1;
            if (whistRound)
            {
                byes = new List<Player>();
                matches = WhistMatchups.GetRoundMatchups(players, r);
            }
            else
            {
                var active = GreedyScheduler.SelectActive(players, matchesPerRound * 4, byeCount, lastByeRound, r);
                byes = players.Where(p => !active.Contains(p)).ToList();
                matches = GreedyScheduler.BuildOneRound(active, partnerCount, opponentCount, matchesPerRound);
            }

            // Cyclic-shift tiebreak only helps with the structural symmetry of Whist matchups.
            GreedyScheduler.AssignCourtsToRound(matches, courtCount, matchesPerRound, r,
                useCyclicShiftTiebreak: whistRound);
            GreedyScheduler.UpdateCounters(matches, partnerCount, opponentCount, courtCount);
            foreach (var b in byes) { byeCount[b.Id]++; lastByeRound[b.Id] = r; }

            output.Add(new Round
            {
                RoundNumber = r + 1,
                Matches = matches,
                Byes = byes.Select(p => new Bye { PlayerId = p.Id }).ToList()
            });
        }

        return new ScheduleResult(output);
    }
}
