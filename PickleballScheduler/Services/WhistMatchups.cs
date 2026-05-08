using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

/// <summary>
/// Cyclic Whist matchups for sizes n in {8, 12, 16, 20, 24}. Returns matchups for round r
/// without court labels — court assignment is delegated to the runtime per-round permutation.
///
/// For n-1 rounds, every pair partners exactly once and every pair opposes exactly twice.
/// Beyond n-1 rounds, callers should fall through to the greedy continuation.
/// </summary>
internal static class WhistMatchups
{
    private static readonly HashSet<int> SupportedSizes = new() { 8, 12, 16, 20, 24 };

    public static bool IsSupportedSize(int playerCount) => SupportedSizes.Contains(playerCount);

    /// <summary>
    /// Returns matchups (no court labels) for round <paramref name="roundIndex"/> (0-based,
    /// max <paramref name="players"/>.Count - 2) of the cyclic Whist schedule.
    /// </summary>
    public static List<Match> GetRoundMatchups(List<Player> players, int roundIndex)
    {
        if (!IsSupportedSize(players.Count))
            throw new ArgumentException($"Unsupported player count: {players.Count}", nameof(players));
        if (roundIndex < 0 || roundIndex >= players.Count - 1)
            throw new ArgumentOutOfRangeException(nameof(roundIndex));

        var baseRound = BaseRounds[players.Count];
        var rotateMod = players.Count - 1;

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

    private static readonly IReadOnlyDictionary<int, BaseMatch[]> BaseRounds =
        new Dictionary<int, BaseMatch[]>
        {
            // Wh(8): players inf, 0..6. Rotation mod 7.
            // Already optimal — see docs/superpowers/specs/2026-05-07-whist-base-round-redesign.md.
            // First-coverage round 3 (theoretical floor).
            [8] = new[]
            {
                new BaseMatch("inf", "0", "1", "3"),
                new BaseMatch("2",   "6", "4", "5"),
            },
            // Wh(12): players inf, 0..10. Rotation mod 11.
            // Redesigned 2026-05-07 to reduce max first-coverage round from 7 to 6.
            [12] = new[]
            {
                new BaseMatch("inf", "0",  "2",  "5"),
                new BaseMatch("1",   "7",  "8",  "9"),
                new BaseMatch("3",  "10",  "4",  "6"),
            },
            // Wh(16): players inf, 0..14. Rotation mod 15.
            // Redesigned 2026-05-07 to reduce max first-coverage round from 12 to 4
            // (8-round improvement; was the user's primary pain point).
            [16] = new[]
            {
                new BaseMatch("inf", "0",  "5",  "10"),
                new BaseMatch("1",   "2",  "4",   "8"),
                new BaseMatch("3",  "11", "12",  "14"),
                new BaseMatch("6",   "9",  "7",  "13"),
            },
            // Wh(20): players inf, 0..18. Rotation mod 19.
            // Redesigned 2026-05-07 to reduce max first-coverage round from 16 to 10.
            [20] = new[]
            {
                new BaseMatch("inf", "0",  "8",  "11"),
                new BaseMatch("1",   "3", "15",  "16"),
                new BaseMatch("2",   "9",  "7",  "18"),
                new BaseMatch("4",  "14", "13",  "17"),
                new BaseMatch("5",  "10",  "6",  "12"),
            },
            // Wh(24): players inf, 0..22. Rotation mod 23.
            // Redesign deferred — search did not complete in available budget.
            // See docs/superpowers/specs/2026-05-07-whist-base-round-redesign.md.
            [24] = new[]
            {
                new BaseMatch("inf", "0",  "1",  "2"),
                new BaseMatch("3",   "14", "4",  "7"),
                new BaseMatch("5",   "20", "12", "17"),
                new BaseMatch("6",   "15", "21", "11"),
                new BaseMatch("8",   "10", "13", "19"),
                new BaseMatch("9",   "16", "18", "22"),
            },
        };
}
