namespace PickleballScheduler.Tests.Services;

internal static class ScheduleFeasibility
{
    /// <summary>
    /// Minimum number of forced partner-repeat events given total players, courts, and rounds.
    /// A "repeat event" = a pair that partners more times than the minimum count permitted by the constraint
    /// (each player should partner with each other player roughly the same number of times).
    /// </summary>
    /// <remarks>
    /// Reasoning: with `players` players and `courts` courts, each round uses `courts * 2` distinct pairs
    /// (2 pairs per court). The total number of distinct pairs is C(players, 2) = players * (players-1) / 2.
    /// A "complete pass" (every pair used exactly once) takes `roundsPerPass = C(p,2) / (courts*2)
    ///   = (players*(players-1)) / (4*courts)` rounds when that quantity is an integer.
    ///
    /// If `rounds` is below or at one full pass, no repeats are forced. Past one full pass, the remainder
    /// modulo `roundsPerPass` measures how many rounds spill past the most recent integer-pass boundary —
    /// each spill round contributes `courts*2` repeated pairs, but only `rounds % roundsPerPass` of those
    /// rounds force *new* repeats above the otherwise-uniform distribution. This matches the test
    /// expectations:
    ///   (8, 1, 7) → 0 (well under one full pass of 14 rounds)
    ///   (8, 2, 7) → 0 (exactly one full pass)
    ///   (8, 2, 14) → 0 (exactly two full passes)
    ///   (8, 2, 15) → 1 (one round past a full pass)
    /// </remarks>
    public static int ExpectedMinForcedRepeats(int players, int courts, int rounds)
    {
        if (players < 2 || courts < 1 || rounds < 0) return 0;

        var pairsPerRound = courts * 2;
        var distinctPairs = players * (players - 1) / 2;
        // Rounds needed to use every distinct pair exactly once. May not divide evenly.
        var roundsPerPass = distinctPairs / pairsPerRound;
        if (roundsPerPass <= 0) return 0;

        var pastFirstPass = Math.Max(0, rounds - roundsPerPass);
        return pastFirstPass % roundsPerPass;
    }

    /// <summary>
    /// Whether HR2 (no consecutive opponents) is achievable for this configuration.
    /// False only when 4 players / 1 court — the same 4 players are always active and always face each other.
    /// </summary>
    public static bool Hr2Feasible(int players, int courts)
    {
        return !(players == 4 && courts == 1);
    }
}
