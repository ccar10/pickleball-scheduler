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
    /// <summary>
    /// Generates a schedule of equal quality to <see cref="Generate"/> but varied by a random seed.
    /// The greedy path is deterministic in player id (it sorts by id and breaks ties by id), so
    /// regenerating with the same players always yields the same matchups. Because schedule quality
    /// (partner/opponent spread, court balance) is invariant under relabeling, we randomly relabel
    /// players to contiguous ids, generate, then map the ids back — producing a different but
    /// equally-good schedule each seed, without touching the tuned generator internals.
    /// </summary>
    public ScheduleResult GenerateShuffled(List<Player> players, int numberOfCourts, int numberOfRounds, int seed)
    {
        var rng = new Random(seed);
        var permuted = players.OrderBy(_ => rng.Next()).ToList();

        var tempPlayers = permuted.Select((p, i) => new Player { Id = i + 1, Name = p.Name }).ToList();
        var tempToReal = new Dictionary<int, int>(permuted.Count);
        for (int i = 0; i < permuted.Count; i++) tempToReal[i + 1] = permuted[i].Id;

        var result = Generate(tempPlayers, numberOfCourts, numberOfRounds);

        foreach (var round in result.Rounds)
        {
            foreach (var m in round.Matches)
            {
                m.Team1Player1Id = tempToReal[m.Team1Player1Id];
                m.Team1Player2Id = tempToReal[m.Team1Player2Id];
                m.Team2Player1Id = tempToReal[m.Team2Player1Id];
                m.Team2Player2Id = tempToReal[m.Team2Player2Id];
            }
            foreach (var b in round.Byes)
                b.PlayerId = tempToReal[b.PlayerId];
        }

        return result;
    }

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
