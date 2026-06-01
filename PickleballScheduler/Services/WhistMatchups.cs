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
        var rotationStep = RotationSteps.GetValueOrDefault(players.Count, 1);

        var matches = new List<Match>(baseRound.Length);
        foreach (var bm in baseRound)
        {
            matches.Add(new Match
            {
                Team1Player1Id = ResolveRole(bm.A, roundIndex, rotateMod, rotationStep, players),
                Team1Player2Id = ResolveRole(bm.B, roundIndex, rotateMod, rotationStep, players),
                Team2Player1Id = ResolveRole(bm.C, roundIndex, rotateMod, rotationStep, players),
                Team2Player2Id = ResolveRole(bm.D, roundIndex, rotateMod, rotationStep, players),
            });
        }
        return matches;
    }

    private static int ResolveRole(string role, int roundIndex, int rotateMod, int rotationStep, List<Player> players)
    {
        if (role == "inf") return players[0].Id;
        var i = int.Parse(role);
        var rotated = (i + roundIndex * rotationStep) % rotateMod;
        return players[1 + rotated].Id;
    }

    private record BaseMatch(string A, string B, string C, string D);

    // Per-round rotation step k applied to finite roles. Any k coprime to (n-1) preserves
    // Whist validity (every finite pair partners once, opposes twice across n-1 rounds),
    // since rotation by k still generates Z_(n-1). Default is 1; override when k > 1 reduces
    // consecutive-round same-foursome overlap.
    private static readonly IReadOnlyDictionary<int, int> RotationSteps = new Dictionary<int, int>
    {
    };

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
            // Redesigned 2026-05-13: prior Wh(16) (max first-coverage 4) had a shift-5
            // self-symmetry in its match-role multiset, causing unordered foursomes to repeat
            // every 5 rounds. New base round adds the foursome-uniqueness constraint at the
            // cost of raising max first-coverage from 4 to 6 (still 6 rounds better than the
            // pre-2026-05-07 value of 12).
            [16] = new[]
            {
                new BaseMatch("inf", "0",  "2",   "8"),
                new BaseMatch("1",   "4",  "5",  "10"),
                new BaseMatch("3",  "14",  "6",  "13"),
                new BaseMatch("7",   "9", "11",  "12"),
            },
            // Wh(20): players inf, 0..18. Rotation mod 19.
            // Redesigned 2026-05-13: prior Wh(20) had two matches whose role sets differed by
            // a fixed shift, causing foursomes from one match to reappear at a later round in
            // another match. New base round enforces foursome uniqueness at the cost of
            // raising max first-coverage from 10 to 11.
            [20] = new[]
            {
                new BaseMatch("inf", "0",  "3",  "11"),
                new BaseMatch("1",   "5", "12",  "18"),
                new BaseMatch("2",   "7",  "8",  "17"),
                new BaseMatch("4",   "6",  "9",  "16"),
                new BaseMatch("10", "13", "14",  "15"),
            },
            // Wh(24): players inf, 0..22. Rotation mod 23.
            // Bounded search 2026-05-30: first valid candidate with max first-coverage ≤ 12,
            // down from the pre-redesign value of 20. The full optimal search is intractable
            // for n=24 with the current enumerator (>260 CPU-hours with no triples completing);
            // the theoretical floor is ⌈23/3⌉ = 8 but the foursome-uniqueness-constrained
            // optimum is unknown. Match 0's finite roles {5, 14, 16} are not consecutive, so
            // the default rotation step (1) keeps back-to-back same-court overlap ≤ 2 without
            // the old step-3 workaround.
            [24] = new[]
            {
                new BaseMatch("inf", "5",  "14", "16"),
                new BaseMatch("0",   "3",  "17", "18"),
                new BaseMatch("1",   "13", "11", "19"),
                new BaseMatch("2",   "21", "9",  "22"),
                new BaseMatch("4",   "10", "6",  "20"),
                new BaseMatch("7",   "12", "8",  "15"),
            },
        };
}
