using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

internal static class WhistCyclicSchedule
{
    private static readonly HashSet<int> SupportedSizes = new() { 8, 12, 16, 20, 24 };

    public static bool IsSupportedSize(int playerCount) => SupportedSizes.Contains(playerCount);

    /// <summary>
    /// Returns matchups (no court numbers) for round <paramref name="roundIndex"/> (0-based,
    /// max <paramref name="players"/>.Count - 2) of the Whist Cyclic schedule.
    /// </summary>
    public static List<Match> GetRoundMatchups(List<Player> players, int roundIndex)
    {
        if (!IsSupportedSize(players.Count))
            throw new ArgumentException($"Unsupported player count: {players.Count}", nameof(players));
        if (roundIndex < 0 || roundIndex >= players.Count - 1)
            throw new ArgumentOutOfRangeException(nameof(roundIndex));

        var baseRound = BaseRounds[players.Count];
        var n = players.Count;
        var rotateMod = n - 1;

        var matches = new List<Match>(baseRound.Length);
        foreach (var bm in baseRound)
        {
            matches.Add(new Match
            {
                Team1Player1Id = ResolveRole(bm.A, roundIndex, rotateMod, players),
                Team1Player2Id = ResolveRole(bm.B, roundIndex, rotateMod, players),
                Team2Player1Id = ResolveRole(bm.C, roundIndex, rotateMod, players),
                Team2Player2Id = ResolveRole(bm.D, roundIndex, rotateMod, players),
            });
        }
        return matches;
    }

    private static int ResolveRole(string role, int roundIndex, int rotateMod, List<Player> players)
    {
        if (role == "inf") return players[0].Id;
        var i = int.Parse(role);
        var rotated = (i + roundIndex) % rotateMod;
        return players[1 + rotated].Id;
    }

    private record BaseMatch(string A, string B, string C, string D);

    // Verified by WhistCyclicScheduleTests.AllPairsPartnerOnceAndOpposeTwice.
    // Sources: standard published Whist Cyclic constructions.
    private static readonly IReadOnlyDictionary<int, BaseMatch[]> BaseRounds =
        new Dictionary<int, BaseMatch[]>
        {
            // Wh(8): players ∞, 0..6. Rotation mod 7.
            [8] = new[]
            {
                new BaseMatch("inf", "0", "1", "3"),
                new BaseMatch("2",   "6", "4", "5"),
            },
        };
}
