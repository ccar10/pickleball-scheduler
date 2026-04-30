using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

/// <summary>
/// Whist Cyclic schedule generator. For supported sizes (n = 8, 12, 16, 20, 24),
/// produces the first n-1 rounds of a Whist tournament: every pair partners
/// exactly once and every pair opposes exactly twice.
/// </summary>
internal static class WhistCyclicSchedule
{
    private static readonly HashSet<int> SupportedSizes = new() { 8, 12, 16, 20, 24 };

    public static bool IsSupportedSize(int playerCount) => SupportedSizes.Contains(playerCount);

    public static List<Match> GetRoundMatchups(List<Player> players, int roundIndex)
    {
        throw new NotImplementedException("GetRoundMatchups is implemented in Task 2.");
    }
}
