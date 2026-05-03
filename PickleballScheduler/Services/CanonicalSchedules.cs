using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public static class CanonicalSchedules
{
    public const int RoundCount = 30;

    private static readonly HashSet<(int players, int courts)> CanonicalPairs = new()
    {
        (8, 2), (12, 3), (16, 4), (20, 5), (24, 6),
    };

    /// <summary>
    /// Returns true when (playerCount, courtCount) is a canonical pair AND its table is populated.
    /// During development the tables may be empty; in that case this returns false and callers
    /// should fall back to the greedy scheduler.
    /// </summary>
    public static bool IsCanonical(int playerCount, int courtCount)
    {
        if (!CanonicalPairs.Contains((playerCount, courtCount))) return false;
        return Schedules.TryGetValue(playerCount, out var table) && table.Length == RoundCount;
    }

    /// <summary>
    /// Returns the matches for round <paramref name="roundIndex"/> (0-based) at the given size,
    /// resolved against <paramref name="players"/>. Caller must ensure <c>IsCanonical</c> is true.
    /// </summary>
    public static List<Match> GetRound(int playerCount, int roundIndex, List<Player> players)
    {
        if (!Schedules.TryGetValue(playerCount, out var table))
            throw new InvalidOperationException($"No table for {playerCount} players");
        if (roundIndex < 0 || roundIndex >= table.Length)
            throw new ArgumentOutOfRangeException(nameof(roundIndex));
        if (players.Count != playerCount)
            throw new ArgumentException(
                $"Expected {playerCount} players, got {players.Count}", nameof(players));

        var bakedRound = table[roundIndex];
        var matches = new List<Match>(bakedRound.Length);
        foreach (var bm in bakedRound)
        {
            matches.Add(new Match
            {
                Team1Player1Id = players[bm.RoleA].Id,
                Team1Player2Id = players[bm.RoleB].Id,
                Team2Player1Id = players[bm.RoleC].Id,
                Team2Player2Id = players[bm.RoleD].Id,
                CourtNumber = bm.Court + 1,
            });
        }
        return matches;
    }

    internal record BakedMatch(int RoleA, int RoleB, int RoleC, int RoleD, int Court);

    // Populated by the offline CanonicalScheduleGenerator (see test project).
    // Empty arrays mean "not yet generated"; IsCanonical returns false until populated.
    private static readonly IReadOnlyDictionary<int, BakedMatch[][]> Schedules =
        new Dictionary<int, BakedMatch[][]>
        {
            [8]  = Array.Empty<BakedMatch[]>(),
            [12] = Array.Empty<BakedMatch[]>(),
            [16] = Array.Empty<BakedMatch[]>(),
            [20] = Array.Empty<BakedMatch[]>(),
            [24] = Array.Empty<BakedMatch[]>(),
        };
}
