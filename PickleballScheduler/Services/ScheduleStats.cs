using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

/// <summary>
/// Read-only analysis of a generated schedule: how often players partner.
/// Shared by the Stats page and the manual-swap repeat warning.
/// </summary>
public static class ScheduleStats
{
    /// <summary>
    /// Counts how many times each unordered pair of players partners (is on the
    /// same team) across all rounds. Keyed by (lowId, highId).
    /// </summary>
    public static Dictionary<(int, int), int> PartnerCounts(IEnumerable<Round> rounds)
    {
        var counts = new Dictionary<(int, int), int>();
        foreach (var round in rounds)
        {
            foreach (var match in round.Matches)
            {
                Add(counts, match.Team1Player1Id, match.Team1Player2Id);
                Add(counts, match.Team2Player1Id, match.Team2Player2Id);
            }
        }
        return counts;
    }

    /// <summary>
    /// The set of player pairs that partner more than once.
    /// </summary>
    public static HashSet<(int, int)> PartnerRepeatPairs(IEnumerable<Round> rounds)
        => PartnerCounts(rounds)
            .Where(kv => kv.Value > 1)
            .Select(kv => kv.Key)
            .ToHashSet();

    private static void Add(Dictionary<(int, int), int> counts, int a, int b)
    {
        var key = a < b ? (a, b) : (b, a);
        counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
    }
}
