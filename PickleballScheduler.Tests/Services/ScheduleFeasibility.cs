namespace PickleballScheduler.Tests.Services;

internal static class ScheduleFeasibility
{
    /// <summary>
    /// Slack tolerance for the HR1 distribution-test threshold — NOT a strict mathematical
    /// lower bound on forced partner repeats. The distribution test asserts
    /// <c>actualHr1Violations &lt;= Hr1RepeatTolerance(...)</c>; this returns a small upper-bound
    /// safety margin tuned so an optimal algorithm reliably stays under it across the test
    /// configuration matrix.
    /// </summary>
    /// <remarks>
    /// Returns 0 when the round count is at or before one full pass (each pair used once);
    /// otherwise <c>(rounds - roundsPerPass) mod roundsPerPass</c>, where
    /// <c>roundsPerPass = floor(C(players,2) / (2*courts))</c>. Truncation in the integer
    /// division of <c>roundsPerPass</c> means this is loose for configurations where
    /// <c>C(players,2) % (2*courts) != 0</c> — that is intentional; the test matrix tolerates
    /// this looseness rather than tightening the assertion.
    ///
    /// Calibrated against:
    ///   (8, 1, 7) → 0 (under one full pass of 14 rounds)
    ///   (8, 2, 7) → 0 (exactly one full pass)
    ///   (8, 2, 14) → 0 (exactly two full passes)
    ///   (8, 2, 15) → 1 (one round past a full pass)
    /// </remarks>
    public static int Hr1RepeatTolerance(int players, int courts, int rounds)
    {
        if (players < 2 || courts < 1 || rounds < 0) return 0;

        var pairsPerRound = courts * 2;
        var distinctPairs = players * (players - 1) / 2;
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
