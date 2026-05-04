using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public static class CanonicalSchedules
{
    /// <summary>Number of rounds in each canonical schedule table.</summary>
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
    /// resolved against <paramref name="players"/>. Caller must ensure <c>IsCanonical</c> is true;
    /// if not, this method throws <see cref="InvalidOperationException"/> or <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    public static List<Match> GetRound(int playerCount, int roundIndex, List<Player> players)
    {
        if (!Schedules.TryGetValue(playerCount, out var table))
            throw new InvalidOperationException($"No table for {playerCount} players");
        if (table.Length == 0)
            throw new InvalidOperationException(
                $"Table for {playerCount} players is not yet populated; check IsCanonical first.");
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
            // (8, 2) — final cost 1000076
            [8]  = new BakedMatch[][]
            {
                new[] { new BakedMatch(0, 1, 3, 7, 1), new BakedMatch(5, 6, 2, 4, 0) },
                new[] { new BakedMatch(3, 4, 2, 1, 1), new BakedMatch(0, 5, 7, 6, 0) },
                new[] { new BakedMatch(0, 1, 6, 2, 0), new BakedMatch(5, 4, 3, 7, 1) },
                new[] { new BakedMatch(6, 3, 1, 4, 0), new BakedMatch(0, 7, 5, 2, 1) },
                new[] { new BakedMatch(4, 0, 6, 5, 1), new BakedMatch(7, 1, 2, 3, 0) },
                new[] { new BakedMatch(0, 2, 4, 7, 0), new BakedMatch(3, 5, 1, 6, 1) },
                new[] { new BakedMatch(4, 1, 2, 7, 0), new BakedMatch(0, 5, 6, 3, 1) },
                new[] { new BakedMatch(4, 2, 7, 6, 0), new BakedMatch(5, 3, 1, 0, 1) },
                new[] { new BakedMatch(3, 4, 0, 5, 0), new BakedMatch(6, 2, 7, 1, 1) },
                new[] { new BakedMatch(6, 5, 4, 7, 0), new BakedMatch(1, 2, 3, 0, 1) },
                new[] { new BakedMatch(1, 6, 5, 2, 1), new BakedMatch(3, 4, 0, 7, 0) },
                new[] { new BakedMatch(5, 7, 6, 2, 0), new BakedMatch(0, 4, 3, 1, 1) },
                new[] { new BakedMatch(5, 4, 2, 1, 0), new BakedMatch(7, 6, 0, 3, 1) },
                new[] { new BakedMatch(6, 0, 5, 7, 0), new BakedMatch(4, 2, 3, 1, 1) },
                new[] { new BakedMatch(0, 7, 5, 4, 1), new BakedMatch(3, 2, 1, 6, 0) },
                new[] { new BakedMatch(1, 7, 0, 6, 0), new BakedMatch(2, 5, 4, 3, 1) },
                new[] { new BakedMatch(5, 1, 4, 6, 1), new BakedMatch(0, 2, 3, 7, 0) },
                new[] { new BakedMatch(7, 5, 1, 0, 0), new BakedMatch(6, 3, 4, 2, 1) },
                new[] { new BakedMatch(0, 4, 6, 7, 1), new BakedMatch(2, 3, 5, 1, 0) },
                new[] { new BakedMatch(2, 0, 1, 3, 0), new BakedMatch(6, 5, 4, 7, 1) },
                new[] { new BakedMatch(6, 4, 3, 2, 0), new BakedMatch(7, 0, 1, 5, 1) },
                new[] { new BakedMatch(2, 7, 4, 1, 1), new BakedMatch(0, 6, 3, 5, 0) },
                new[] { new BakedMatch(0, 3, 4, 7, 0), new BakedMatch(2, 5, 1, 6, 1) },
                new[] { new BakedMatch(0, 1, 5, 6, 0), new BakedMatch(3, 4, 2, 7, 1) },
                new[] { new BakedMatch(5, 3, 4, 6, 0), new BakedMatch(7, 0, 1, 2, 1) },
                new[] { new BakedMatch(5, 4, 7, 1, 0), new BakedMatch(2, 0, 6, 3, 1) },
                new[] { new BakedMatch(1, 5, 0, 4, 1), new BakedMatch(6, 7, 2, 3, 0) },
                new[] { new BakedMatch(5, 4, 1, 3, 0), new BakedMatch(0, 6, 7, 2, 1) },
                new[] { new BakedMatch(3, 0, 1, 4, 0), new BakedMatch(5, 7, 2, 6, 1) },
                new[] { new BakedMatch(6, 4, 3, 7, 1), new BakedMatch(1, 2, 5, 0, 0) },
            },
            // (12, 3) — final cost 4000110 (partner spread 2, court spread 0)
            [12] = new BakedMatch[][]
            {
                new[] { new BakedMatch(0, 6, 8, 9, 1), new BakedMatch(2, 4, 7, 5, 0), new BakedMatch(3, 1, 11, 10, 2) },
                new[] { new BakedMatch(3, 2, 4, 8, 1), new BakedMatch(7, 1, 6, 5, 2), new BakedMatch(11, 9, 10, 0, 0) },
                new[] { new BakedMatch(1, 6, 7, 10, 1), new BakedMatch(9, 2, 8, 3, 2), new BakedMatch(5, 4, 0, 11, 0) },
                new[] { new BakedMatch(11, 10, 9, 0, 0), new BakedMatch(7, 5, 1, 4, 1), new BakedMatch(3, 2, 8, 6, 2) },
                new[] { new BakedMatch(1, 5, 7, 9, 2), new BakedMatch(10, 2, 6, 3, 0), new BakedMatch(0, 4, 11, 8, 1) },
                new[] { new BakedMatch(10, 1, 4, 8, 0), new BakedMatch(2, 3, 9, 5, 1), new BakedMatch(11, 7, 6, 0, 2) },
                new[] { new BakedMatch(8, 2, 3, 10, 0), new BakedMatch(5, 6, 1, 9, 1), new BakedMatch(4, 7, 0, 11, 2) },
                new[] { new BakedMatch(3, 9, 7, 11, 1), new BakedMatch(6, 1, 5, 2, 0), new BakedMatch(0, 4, 8, 10, 2) },
                new[] { new BakedMatch(4, 7, 6, 10, 0), new BakedMatch(3, 11, 0, 5, 1), new BakedMatch(1, 2, 9, 8, 2) },
                new[] { new BakedMatch(3, 0, 4, 2, 0), new BakedMatch(10, 5, 11, 6, 2), new BakedMatch(9, 1, 8, 7, 1) },
                new[] { new BakedMatch(9, 0, 11, 5, 2), new BakedMatch(8, 3, 1, 7, 0), new BakedMatch(6, 2, 4, 10, 1) },
                new[] { new BakedMatch(11, 7, 2, 3, 1), new BakedMatch(1, 9, 10, 6, 0), new BakedMatch(0, 4, 5, 8, 2) },
                new[] { new BakedMatch(2, 6, 11, 10, 2), new BakedMatch(9, 0, 3, 7, 0), new BakedMatch(5, 4, 1, 8, 1) },
                new[] { new BakedMatch(5, 7, 8, 0, 1), new BakedMatch(9, 4, 2, 11, 2), new BakedMatch(10, 6, 1, 3, 0) },
                new[] { new BakedMatch(6, 9, 10, 8, 2), new BakedMatch(7, 3, 0, 1, 1), new BakedMatch(4, 2, 11, 5, 0) },
                new[] { new BakedMatch(4, 3, 5, 6, 2), new BakedMatch(9, 1, 11, 8, 0), new BakedMatch(7, 2, 10, 0, 1) },
                new[] { new BakedMatch(5, 0, 2, 11, 0), new BakedMatch(8, 4, 7, 1, 1), new BakedMatch(6, 10, 3, 9, 2) },
                new[] { new BakedMatch(0, 2, 4, 1, 0), new BakedMatch(9, 5, 8, 6, 1), new BakedMatch(11, 10, 7, 3, 2) },
                new[] { new BakedMatch(10, 5, 11, 7, 0), new BakedMatch(8, 0, 3, 9, 1), new BakedMatch(1, 2, 6, 4, 2) },
                new[] { new BakedMatch(5, 0, 2, 9, 2), new BakedMatch(8, 3, 10, 4, 1), new BakedMatch(1, 11, 6, 7, 0) },
                new[] { new BakedMatch(4, 1, 0, 2, 2), new BakedMatch(3, 6, 5, 11, 1), new BakedMatch(9, 10, 8, 7, 0) },
                new[] { new BakedMatch(11, 9, 7, 0, 0), new BakedMatch(3, 8, 1, 10, 2), new BakedMatch(2, 5, 4, 6, 1) },
                new[] { new BakedMatch(11, 6, 10, 5, 1), new BakedMatch(9, 4, 1, 8, 2), new BakedMatch(2, 0, 3, 7, 0) },
                new[] { new BakedMatch(3, 4, 1, 7, 2), new BakedMatch(10, 2, 11, 0, 1), new BakedMatch(6, 5, 9, 8, 0) },
                new[] { new BakedMatch(0, 7, 11, 5, 2), new BakedMatch(9, 4, 1, 8, 0), new BakedMatch(3, 10, 6, 2, 1) },
                new[] { new BakedMatch(6, 7, 4, 8, 0), new BakedMatch(10, 9, 2, 11, 1), new BakedMatch(5, 1, 3, 0, 2) },
                new[] { new BakedMatch(0, 4, 11, 8, 2), new BakedMatch(7, 10, 6, 9, 1), new BakedMatch(1, 3, 5, 2, 0) },
                new[] { new BakedMatch(1, 4, 11, 0, 1), new BakedMatch(2, 7, 10, 5, 2), new BakedMatch(6, 3, 8, 9, 0) },
                new[] { new BakedMatch(2, 4, 11, 1, 1), new BakedMatch(6, 0, 8, 5, 0), new BakedMatch(3, 10, 9, 7, 2) },
                new[] { new BakedMatch(1, 0, 9, 10, 1), new BakedMatch(6, 7, 2, 8, 2), new BakedMatch(4, 5, 11, 3, 0) },
            },
            // (16, 4) — final cost 9256006 (partner spread 3, average per-player court spread 1)
            [16] = new BakedMatch[][]
            {
                new[] { new BakedMatch(11, 8, 4, 12, 0), new BakedMatch(14, 3, 6, 9, 3), new BakedMatch(10, 7, 1, 0, 2), new BakedMatch(5, 15, 2, 13, 1) },
                new[] { new BakedMatch(12, 14, 15, 11, 3), new BakedMatch(10, 9, 6, 8, 1), new BakedMatch(3, 4, 5, 7, 0), new BakedMatch(0, 13, 2, 1, 2) },
                new[] { new BakedMatch(7, 3, 11, 14, 2), new BakedMatch(12, 5, 15, 8, 0), new BakedMatch(9, 6, 13, 4, 3), new BakedMatch(2, 0, 1, 10, 1) },
                new[] { new BakedMatch(15, 5, 10, 3, 3), new BakedMatch(14, 12, 13, 6, 0), new BakedMatch(11, 8, 4, 7, 2), new BakedMatch(0, 1, 2, 9, 1) },
                new[] { new BakedMatch(1, 14, 15, 8, 0), new BakedMatch(0, 2, 4, 6, 2), new BakedMatch(11, 12, 3, 13, 1), new BakedMatch(5, 10, 9, 7, 3) },
                new[] { new BakedMatch(4, 1, 7, 3, 3), new BakedMatch(11, 5, 14, 8, 2), new BakedMatch(9, 15, 2, 6, 1), new BakedMatch(13, 12, 0, 10, 0) },
                new[] { new BakedMatch(9, 4, 0, 12, 2), new BakedMatch(5, 14, 7, 1, 3), new BakedMatch(6, 10, 13, 8, 0), new BakedMatch(3, 11, 2, 15, 1) },
                new[] { new BakedMatch(3, 6, 8, 2, 0), new BakedMatch(12, 7, 5, 9, 1), new BakedMatch(11, 14, 13, 0, 3), new BakedMatch(10, 15, 1, 4, 2) },
                new[] { new BakedMatch(2, 10, 9, 15, 0), new BakedMatch(1, 8, 0, 7, 3), new BakedMatch(14, 5, 4, 3, 2), new BakedMatch(11, 6, 12, 13, 1) },
                new[] { new BakedMatch(12, 0, 15, 6, 3), new BakedMatch(3, 8, 1, 2, 0), new BakedMatch(14, 4, 9, 10, 1), new BakedMatch(5, 7, 13, 11, 2) },
                new[] { new BakedMatch(7, 15, 14, 3, 0), new BakedMatch(11, 9, 10, 12, 2), new BakedMatch(1, 5, 13, 6, 1), new BakedMatch(8, 2, 0, 4, 3) },
                new[] { new BakedMatch(12, 0, 5, 7, 1), new BakedMatch(1, 13, 4, 2, 3), new BakedMatch(9, 10, 14, 8, 0), new BakedMatch(6, 15, 3, 11, 2) },
                new[] { new BakedMatch(8, 15, 11, 7, 3), new BakedMatch(5, 12, 2, 10, 2), new BakedMatch(3, 9, 0, 13, 0), new BakedMatch(6, 1, 4, 14, 1) },
                new[] { new BakedMatch(14, 5, 1, 4, 0), new BakedMatch(2, 7, 10, 11, 1), new BakedMatch(8, 13, 15, 12, 3), new BakedMatch(0, 9, 3, 6, 2) },
                new[] { new BakedMatch(8, 7, 13, 6, 1), new BakedMatch(0, 2, 12, 14, 2), new BakedMatch(15, 11, 1, 9, 0), new BakedMatch(4, 10, 5, 3, 3) },
                new[] { new BakedMatch(7, 14, 12, 15, 1), new BakedMatch(0, 4, 3, 13, 3), new BakedMatch(6, 8, 10, 9, 2), new BakedMatch(11, 2, 1, 5, 0) },
                new[] { new BakedMatch(14, 9, 5, 3, 0), new BakedMatch(7, 8, 0, 6, 3), new BakedMatch(2, 10, 15, 4, 1), new BakedMatch(11, 12, 1, 13, 2) },
                new[] { new BakedMatch(6, 9, 0, 13, 0), new BakedMatch(14, 7, 5, 12, 2), new BakedMatch(3, 2, 10, 11, 3), new BakedMatch(15, 1, 8, 4, 1) },
                new[] { new BakedMatch(15, 7, 8, 6, 2), new BakedMatch(13, 12, 4, 0, 0), new BakedMatch(2, 1, 14, 10, 3), new BakedMatch(5, 11, 3, 9, 1) },
                new[] { new BakedMatch(15, 13, 1, 9, 2), new BakedMatch(4, 12, 2, 3, 0), new BakedMatch(11, 10, 14, 6, 3), new BakedMatch(8, 0, 5, 7, 1) },
                new[] { new BakedMatch(2, 11, 0, 9, 0), new BakedMatch(12, 7, 1, 14, 1), new BakedMatch(4, 3, 10, 6, 3), new BakedMatch(13, 5, 8, 15, 2) },
                new[] { new BakedMatch(9, 8, 15, 2, 2), new BakedMatch(11, 14, 12, 5, 3), new BakedMatch(7, 13, 1, 3, 0), new BakedMatch(4, 10, 0, 6, 1) },
                new[] { new BakedMatch(12, 1, 3, 4, 2), new BakedMatch(7, 10, 9, 13, 3), new BakedMatch(0, 14, 11, 8, 1), new BakedMatch(5, 15, 6, 2, 0) },
                new[] { new BakedMatch(14, 1, 3, 9, 2), new BakedMatch(7, 11, 4, 10, 0), new BakedMatch(0, 15, 13, 5, 1), new BakedMatch(6, 8, 2, 12, 3) },
                new[] { new BakedMatch(9, 2, 12, 1, 3), new BakedMatch(0, 7, 4, 15, 0), new BakedMatch(14, 3, 8, 13, 1), new BakedMatch(5, 11, 10, 6, 2) },
                new[] { new BakedMatch(14, 1, 0, 6, 0), new BakedMatch(10, 5, 12, 13, 2), new BakedMatch(8, 2, 11, 3, 1), new BakedMatch(15, 9, 7, 4, 3) },
                new[] { new BakedMatch(1, 3, 10, 6, 1), new BakedMatch(9, 11, 7, 8, 0), new BakedMatch(15, 12, 5, 0, 3), new BakedMatch(4, 2, 13, 14, 2) },
                new[] { new BakedMatch(6, 1, 11, 5, 3), new BakedMatch(2, 0, 3, 14, 2), new BakedMatch(9, 8, 4, 13, 1), new BakedMatch(7, 15, 10, 12, 0) },
                new[] { new BakedMatch(6, 5, 2, 1, 0), new BakedMatch(4, 10, 14, 12, 1), new BakedMatch(7, 8, 13, 3, 2), new BakedMatch(9, 0, 15, 11, 3) },
                new[] { new BakedMatch(1, 3, 12, 9, 1), new BakedMatch(4, 6, 15, 7, 2), new BakedMatch(2, 14, 8, 13, 3), new BakedMatch(11, 0, 5, 10, 0) },
            },
            // (20, 5) — final cost 4000280 (partner spread 2, court spread 0)
            [20] = new BakedMatch[][]
            {
                new[] { new BakedMatch(7, 11, 10, 18, 4), new BakedMatch(14, 13, 17, 9, 1), new BakedMatch(4, 0, 6, 5, 2), new BakedMatch(19, 8, 12, 16, 0), new BakedMatch(1, 3, 2, 15, 3) },
                new[] { new BakedMatch(8, 2, 5, 13, 4), new BakedMatch(0, 14, 9, 15, 0), new BakedMatch(6, 7, 3, 18, 2), new BakedMatch(4, 16, 12, 10, 1), new BakedMatch(19, 1, 11, 17, 3) },
                new[] { new BakedMatch(18, 16, 12, 1, 1), new BakedMatch(6, 3, 14, 9, 2), new BakedMatch(13, 11, 15, 4, 4), new BakedMatch(8, 19, 0, 7, 3), new BakedMatch(2, 10, 17, 5, 0) },
                new[] { new BakedMatch(0, 6, 11, 15, 1), new BakedMatch(14, 13, 9, 12, 4), new BakedMatch(18, 7, 19, 3, 0), new BakedMatch(5, 4, 17, 8, 2), new BakedMatch(10, 1, 16, 2, 3) },
                new[] { new BakedMatch(7, 0, 2, 17, 4), new BakedMatch(1, 13, 9, 19, 2), new BakedMatch(8, 15, 14, 5, 3), new BakedMatch(10, 11, 4, 6, 1), new BakedMatch(16, 3, 18, 12, 0) },
                new[] { new BakedMatch(14, 7, 8, 11, 0), new BakedMatch(18, 2, 15, 1, 4), new BakedMatch(19, 0, 12, 17, 2), new BakedMatch(4, 16, 3, 5, 1), new BakedMatch(9, 10, 6, 13, 3) },
                new[] { new BakedMatch(12, 0, 8, 14, 3), new BakedMatch(2, 7, 9, 17, 1), new BakedMatch(19, 18, 6, 16, 4), new BakedMatch(15, 3, 4, 1, 0), new BakedMatch(11, 10, 5, 13, 2) },
                new[] { new BakedMatch(14, 7, 0, 19, 4), new BakedMatch(11, 4, 2, 8, 3), new BakedMatch(18, 9, 1, 15, 1), new BakedMatch(16, 17, 6, 5, 0), new BakedMatch(3, 12, 10, 13, 2) },
                new[] { new BakedMatch(3, 19, 8, 13, 4), new BakedMatch(12, 15, 4, 2, 1), new BakedMatch(17, 6, 1, 5, 3), new BakedMatch(14, 0, 18, 10, 0), new BakedMatch(9, 7, 16, 11, 2) },
                new[] { new BakedMatch(9, 14, 1, 4, 4), new BakedMatch(6, 2, 18, 8, 0), new BakedMatch(11, 13, 12, 3, 1), new BakedMatch(15, 0, 19, 5, 3), new BakedMatch(17, 7, 10, 16, 2) },
                new[] { new BakedMatch(1, 11, 8, 16, 0), new BakedMatch(13, 19, 5, 7, 1), new BakedMatch(2, 15, 18, 3, 2), new BakedMatch(14, 12, 9, 4, 4), new BakedMatch(10, 0, 6, 17, 3) },
                new[] { new BakedMatch(6, 18, 0, 4, 2), new BakedMatch(11, 12, 7, 10, 3), new BakedMatch(13, 15, 9, 17, 0), new BakedMatch(19, 2, 8, 14, 1), new BakedMatch(1, 5, 3, 16, 4) },
                new[] { new BakedMatch(3, 10, 4, 18, 4), new BakedMatch(7, 5, 16, 9, 0), new BakedMatch(1, 14, 8, 11, 2), new BakedMatch(19, 13, 6, 15, 1), new BakedMatch(0, 2, 17, 12, 3) },
                new[] { new BakedMatch(17, 5, 7, 19, 1), new BakedMatch(14, 11, 6, 2, 0), new BakedMatch(18, 9, 8, 13, 3), new BakedMatch(16, 0, 3, 12, 4), new BakedMatch(1, 15, 10, 4, 2) },
                new[] { new BakedMatch(5, 15, 7, 0, 2), new BakedMatch(17, 1, 18, 16, 1), new BakedMatch(14, 4, 9, 8, 4), new BakedMatch(13, 6, 10, 3, 0), new BakedMatch(2, 11, 12, 19, 3) },
                new[] { new BakedMatch(12, 9, 8, 1, 1), new BakedMatch(0, 18, 4, 10, 2), new BakedMatch(16, 6, 7, 13, 3), new BakedMatch(17, 2, 11, 15, 4), new BakedMatch(3, 5, 19, 14, 0) },
                new[] { new BakedMatch(10, 16, 15, 9, 3), new BakedMatch(11, 1, 14, 4, 0), new BakedMatch(8, 12, 5, 2, 2), new BakedMatch(3, 6, 0, 19, 1), new BakedMatch(18, 13, 7, 17, 4) },
                new[] { new BakedMatch(17, 6, 2, 11, 2), new BakedMatch(7, 8, 14, 18, 1), new BakedMatch(12, 0, 15, 10, 0), new BakedMatch(13, 16, 19, 1, 4), new BakedMatch(4, 5, 3, 9, 3) },
                new[] { new BakedMatch(17, 15, 5, 9, 4), new BakedMatch(18, 7, 8, 2, 0), new BakedMatch(12, 4, 10, 0, 1), new BakedMatch(1, 16, 19, 11, 2), new BakedMatch(14, 13, 3, 6, 3) },
                new[] { new BakedMatch(9, 7, 0, 8, 2), new BakedMatch(6, 4, 1, 5, 4), new BakedMatch(13, 19, 12, 10, 0), new BakedMatch(14, 16, 17, 3, 1), new BakedMatch(15, 18, 2, 11, 3) },
                new[] { new BakedMatch(1, 16, 7, 17, 3), new BakedMatch(10, 6, 0, 3, 4), new BakedMatch(13, 9, 19, 11, 0), new BakedMatch(15, 5, 18, 8, 1), new BakedMatch(14, 12, 2, 4, 2) },
                new[] { new BakedMatch(4, 13, 0, 6, 1), new BakedMatch(2, 1, 7, 5, 0), new BakedMatch(17, 18, 9, 10, 4), new BakedMatch(19, 8, 11, 14, 3), new BakedMatch(15, 12, 16, 3, 2) },
                new[] { new BakedMatch(11, 0, 6, 10, 1), new BakedMatch(16, 15, 3, 8, 4), new BakedMatch(5, 2, 9, 14, 2), new BakedMatch(7, 19, 12, 4, 3), new BakedMatch(17, 13, 18, 1, 0) },
                new[] { new BakedMatch(17, 10, 9, 15, 2), new BakedMatch(11, 6, 0, 2, 4), new BakedMatch(19, 12, 1, 4, 0), new BakedMatch(14, 3, 18, 16, 3), new BakedMatch(8, 5, 7, 13, 1) },
                new[] { new BakedMatch(13, 11, 19, 16, 2), new BakedMatch(18, 3, 14, 12, 3), new BakedMatch(8, 7, 9, 1, 1), new BakedMatch(4, 6, 17, 0, 0), new BakedMatch(10, 2, 5, 15, 4) },
                new[] { new BakedMatch(4, 7, 10, 3, 3), new BakedMatch(14, 17, 19, 6, 2), new BakedMatch(5, 16, 2, 18, 1), new BakedMatch(15, 11, 13, 9, 0), new BakedMatch(12, 8, 1, 0, 4) },
                new[] { new BakedMatch(18, 8, 13, 7, 2), new BakedMatch(4, 10, 2, 9, 0), new BakedMatch(17, 11, 5, 12, 4), new BakedMatch(3, 1, 14, 19, 1), new BakedMatch(15, 6, 16, 0, 3) },
                new[] { new BakedMatch(0, 3, 11, 14, 1), new BakedMatch(15, 4, 8, 7, 0), new BakedMatch(10, 19, 16, 6, 4), new BakedMatch(18, 9, 17, 5, 3), new BakedMatch(12, 13, 1, 2, 2) },
                new[] { new BakedMatch(13, 18, 5, 4, 3), new BakedMatch(10, 2, 9, 11, 1), new BakedMatch(17, 16, 0, 6, 0), new BakedMatch(3, 15, 8, 1, 2), new BakedMatch(12, 7, 19, 14, 4) },
                new[] { new BakedMatch(14, 18, 19, 16, 2), new BakedMatch(1, 13, 9, 4, 3), new BakedMatch(7, 11, 8, 6, 4), new BakedMatch(15, 10, 2, 17, 1), new BakedMatch(5, 3, 0, 12, 0) },
            },
            // (24, 6) — final cost 4000387 (partner spread 2, court spread 0)
            [24] = new BakedMatch[][]
            {
                new[] { new BakedMatch(5, 3, 2, 16, 2), new BakedMatch(12, 17, 21, 20, 1), new BakedMatch(6, 23, 11, 22, 5), new BakedMatch(8, 1, 7, 13, 4), new BakedMatch(9, 0, 14, 10, 0), new BakedMatch(19, 15, 18, 4, 3) },
                new[] { new BakedMatch(7, 23, 5, 16, 1), new BakedMatch(21, 4, 12, 9, 2), new BakedMatch(2, 11, 15, 1, 0), new BakedMatch(10, 18, 17, 8, 5), new BakedMatch(0, 19, 20, 22, 4), new BakedMatch(3, 14, 6, 13, 3) },
                new[] { new BakedMatch(17, 9, 7, 6, 1), new BakedMatch(1, 14, 19, 10, 2), new BakedMatch(18, 11, 12, 4, 3), new BakedMatch(16, 3, 22, 8, 4), new BakedMatch(20, 2, 13, 21, 5), new BakedMatch(5, 23, 0, 15, 0) },
                new[] { new BakedMatch(11, 1, 2, 16, 1), new BakedMatch(10, 17, 22, 0, 0), new BakedMatch(13, 23, 14, 9, 4), new BakedMatch(3, 15, 21, 6, 2), new BakedMatch(8, 18, 19, 7, 5), new BakedMatch(4, 5, 20, 12, 3) },
                new[] { new BakedMatch(14, 13, 20, 1, 0), new BakedMatch(18, 15, 21, 23, 4), new BakedMatch(12, 11, 19, 8, 1), new BakedMatch(3, 0, 16, 9, 5), new BakedMatch(4, 6, 5, 7, 2), new BakedMatch(22, 17, 2, 10, 3) },
                new[] { new BakedMatch(5, 1, 2, 14, 2), new BakedMatch(23, 18, 9, 17, 4), new BakedMatch(20, 6, 21, 0, 3), new BakedMatch(8, 4, 3, 16, 0), new BakedMatch(10, 11, 7, 12, 5), new BakedMatch(15, 13, 19, 22, 1) },
                new[] { new BakedMatch(7, 18, 13, 9, 0), new BakedMatch(6, 21, 11, 19, 4), new BakedMatch(14, 5, 10, 0, 1), new BakedMatch(1, 17, 15, 12, 3), new BakedMatch(20, 2, 8, 3, 5), new BakedMatch(16, 22, 4, 23, 2) },
                new[] { new BakedMatch(23, 8, 12, 3, 2), new BakedMatch(7, 2, 13, 20, 3), new BakedMatch(5, 18, 0, 11, 5), new BakedMatch(6, 16, 22, 19, 4), new BakedMatch(14, 21, 4, 1, 0), new BakedMatch(15, 10, 9, 17, 1) },
                new[] { new BakedMatch(20, 2, 23, 11, 2), new BakedMatch(14, 5, 9, 15, 0), new BakedMatch(6, 19, 17, 1, 5), new BakedMatch(0, 4, 21, 7, 4), new BakedMatch(16, 22, 13, 12, 1), new BakedMatch(8, 3, 18, 10, 3) },
                new[] { new BakedMatch(23, 17, 1, 2, 4), new BakedMatch(20, 16, 14, 22, 1), new BakedMatch(4, 12, 5, 6, 5), new BakedMatch(21, 9, 15, 3, 3), new BakedMatch(0, 11, 18, 19, 2), new BakedMatch(10, 7, 13, 8, 0) },
                new[] { new BakedMatch(1, 0, 2, 22, 3), new BakedMatch(6, 16, 19, 12, 0), new BakedMatch(7, 23, 9, 8, 5), new BakedMatch(18, 21, 13, 3, 4), new BakedMatch(10, 15, 11, 4, 2), new BakedMatch(20, 5, 17, 14, 1) },
                new[] { new BakedMatch(7, 13, 17, 0, 4), new BakedMatch(9, 1, 19, 6, 3), new BakedMatch(22, 15, 5, 16, 5), new BakedMatch(11, 23, 12, 4, 1), new BakedMatch(8, 10, 14, 21, 2), new BakedMatch(18, 20, 3, 2, 0) },
                new[] { new BakedMatch(19, 7, 1, 11, 3), new BakedMatch(5, 3, 14, 9, 0), new BakedMatch(6, 18, 15, 4, 4), new BakedMatch(0, 22, 21, 2, 5), new BakedMatch(23, 16, 17, 8, 1), new BakedMatch(12, 10, 13, 20, 2) },
                new[] { new BakedMatch(18, 9, 8, 7, 1), new BakedMatch(6, 4, 14, 17, 4), new BakedMatch(3, 19, 13, 1, 2), new BakedMatch(23, 0, 5, 16, 3), new BakedMatch(21, 15, 10, 11, 0), new BakedMatch(22, 2, 20, 12, 5) },
                new[] { new BakedMatch(20, 0, 22, 11, 4), new BakedMatch(8, 18, 15, 19, 1), new BakedMatch(21, 12, 13, 2, 0), new BakedMatch(7, 5, 23, 3, 3), new BakedMatch(6, 1, 17, 16, 2), new BakedMatch(9, 10, 14, 4, 5) },
                new[] { new BakedMatch(20, 14, 15, 11, 4), new BakedMatch(10, 21, 23, 19, 1), new BakedMatch(13, 1, 17, 2, 5), new BakedMatch(6, 18, 16, 12, 0), new BakedMatch(7, 9, 8, 5, 2), new BakedMatch(4, 22, 3, 0, 3) },
                new[] { new BakedMatch(15, 0, 20, 23, 0), new BakedMatch(8, 7, 22, 17, 2), new BakedMatch(14, 3, 19, 21, 5), new BakedMatch(18, 10, 1, 16, 4), new BakedMatch(9, 12, 13, 11, 3), new BakedMatch(4, 5, 2, 6, 1) },
                new[] { new BakedMatch(8, 11, 14, 7, 3), new BakedMatch(10, 15, 23, 16, 5), new BakedMatch(4, 22, 5, 19, 0), new BakedMatch(21, 17, 1, 20, 2), new BakedMatch(2, 9, 3, 12, 4), new BakedMatch(13, 18, 6, 0, 1) },
                new[] { new BakedMatch(0, 15, 22, 19, 1), new BakedMatch(9, 20, 21, 2, 2), new BakedMatch(17, 12, 23, 4, 0), new BakedMatch(7, 18, 3, 13, 5), new BakedMatch(1, 16, 14, 8, 3), new BakedMatch(10, 6, 5, 11, 4) },
                new[] { new BakedMatch(17, 6, 21, 10, 3), new BakedMatch(11, 8, 16, 22, 0), new BakedMatch(20, 12, 2, 3, 1), new BakedMatch(9, 4, 0, 13, 2), new BakedMatch(19, 15, 14, 18, 5), new BakedMatch(5, 23, 1, 7, 4) },
                new[] { new BakedMatch(23, 17, 12, 13, 5), new BakedMatch(14, 16, 22, 21, 3), new BakedMatch(7, 10, 8, 0, 2), new BakedMatch(3, 5, 15, 11, 4), new BakedMatch(2, 4, 18, 6, 1), new BakedMatch(20, 1, 9, 19, 0) },
                new[] { new BakedMatch(18, 17, 19, 12, 2), new BakedMatch(1, 3, 16, 20, 5), new BakedMatch(14, 9, 7, 15, 1), new BakedMatch(21, 8, 2, 5, 4), new BakedMatch(11, 10, 13, 0, 3), new BakedMatch(6, 4, 23, 22, 0) },
                new[] { new BakedMatch(7, 3, 18, 15, 2), new BakedMatch(20, 14, 16, 13, 4), new BakedMatch(5, 9, 2, 12, 3), new BakedMatch(0, 11, 10, 22, 1), new BakedMatch(6, 17, 23, 4, 5), new BakedMatch(19, 8, 1, 21, 0) },
                new[] { new BakedMatch(2, 13, 23, 21, 1), new BakedMatch(9, 18, 11, 14, 2), new BakedMatch(16, 6, 15, 20, 3), new BakedMatch(22, 3, 12, 17, 0), new BakedMatch(10, 8, 5, 7, 5), new BakedMatch(4, 0, 1, 19, 4) },
                new[] { new BakedMatch(7, 8, 17, 18, 3), new BakedMatch(11, 13, 4, 20, 1), new BakedMatch(6, 15, 21, 1, 5), new BakedMatch(3, 19, 23, 2, 0), new BakedMatch(5, 14, 10, 9, 4), new BakedMatch(12, 22, 0, 16, 2) },
                new[] { new BakedMatch(9, 20, 8, 12, 4), new BakedMatch(3, 10, 4, 1, 1), new BakedMatch(14, 22, 17, 15, 3), new BakedMatch(0, 18, 23, 6, 2), new BakedMatch(7, 21, 13, 2, 0), new BakedMatch(16, 11, 5, 19, 5) },
                new[] { new BakedMatch(23, 13, 10, 20, 3), new BakedMatch(12, 15, 4, 2, 4), new BakedMatch(18, 5, 11, 7, 0), new BakedMatch(16, 17, 19, 6, 2), new BakedMatch(9, 22, 0, 21, 5), new BakedMatch(14, 8, 3, 1, 1) },
                new[] { new BakedMatch(10, 12, 16, 8, 4), new BakedMatch(6, 17, 7, 20, 0), new BakedMatch(4, 14, 0, 9, 5), new BakedMatch(13, 15, 22, 11, 2), new BakedMatch(19, 23, 21, 2, 3), new BakedMatch(5, 1, 18, 3, 1) },
                new[] { new BakedMatch(6, 21, 1, 7, 1), new BakedMatch(2, 14, 5, 20, 2), new BakedMatch(10, 22, 3, 17, 4), new BakedMatch(16, 0, 18, 11, 0), new BakedMatch(4, 13, 15, 12, 5), new BakedMatch(9, 23, 8, 19, 3) },
                new[] { new BakedMatch(21, 3, 9, 0, 1), new BakedMatch(16, 18, 5, 4, 3), new BakedMatch(2, 12, 19, 7, 4), new BakedMatch(13, 23, 15, 22, 2), new BakedMatch(1, 11, 20, 14, 5), new BakedMatch(17, 10, 6, 8, 0) },
            },
        };
}
