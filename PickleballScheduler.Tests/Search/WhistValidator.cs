namespace PickleballScheduler.Tests.Search;

internal static class WhistValidator
{
    /// <summary>
    /// Returns true iff the candidate forms a valid Whist tournament when rotated n-1 times:
    /// every pair partners exactly once, every pair opposes exactly twice. Failure reason
    /// is set when false.
    /// </summary>
    public static bool IsValid(BaseRoundCandidate c, out string reason)
    {
        var n = c.PlayerCount;
        var rotateMod = c.RotateMod;
        var maxClass = rotateMod / 2;

        // 1. Role set is exactly {Inf} ∪ {0..n-2}.
        var seen = new HashSet<int>();
        int infCount = 0;
        foreach (var m in c.Matches)
        {
            foreach (var r in new[] { m.Team1A, m.Team1B, m.Team2A, m.Team2B })
            {
                if (r == BaseRoundCandidate.Inf) { infCount++; continue; }
                if (r < 0 || r >= rotateMod) { reason = $"role {r} out of range"; return false; }
                if (!seen.Add(r)) { reason = $"duplicate role {r}"; return false; }
            }
        }
        if (infCount != 1) { reason = $"expected 1 inf, got {infCount}"; return false; }
        if (seen.Count != rotateMod) { reason = $"expected {rotateMod} finite roles, got {seen.Count}"; return false; }

        // 2. Finite partner-pair difference classes form a permutation of {1..maxClass}.
        var partnerClasses = new int[maxClass + 1];
        foreach (var m in c.Matches)
        {
            CountPartnerPair(m.Team1A, m.Team1B, rotateMod, partnerClasses);
            CountPartnerPair(m.Team2A, m.Team2B, rotateMod, partnerClasses);
        }
        for (int k = 1; k <= maxClass; k++)
        {
            if (partnerClasses[k] != 1)
            {
                reason = $"partner difference class {k} hit {partnerClasses[k]} times, expected 1";
                return false;
            }
        }

        // 3. Finite opponent-pair difference classes hit each {1..maxClass} exactly twice.
        var opponentClasses = new int[maxClass + 1];
        foreach (var m in c.Matches)
        {
            CountOpponentPair(m.Team1A, m.Team2A, rotateMod, opponentClasses);
            CountOpponentPair(m.Team1A, m.Team2B, rotateMod, opponentClasses);
            CountOpponentPair(m.Team1B, m.Team2A, rotateMod, opponentClasses);
            CountOpponentPair(m.Team1B, m.Team2B, rotateMod, opponentClasses);
        }
        for (int k = 1; k <= maxClass; k++)
        {
            if (opponentClasses[k] != 2)
            {
                reason = $"opponent difference class {k} hit {opponentClasses[k]} times, expected 2";
                return false;
            }
        }

        reason = "";
        return true;
    }

    private static void CountPartnerPair(int a, int b, int rotateMod, int[] classes)
    {
        if (a == BaseRoundCandidate.Inf || b == BaseRoundCandidate.Inf) return;
        var k = Difference.Class(a, b, rotateMod);
        if (k > 0 && k < classes.Length) classes[k]++;
    }

    private static void CountOpponentPair(int a, int b, int rotateMod, int[] classes)
    {
        if (a == BaseRoundCandidate.Inf || b == BaseRoundCandidate.Inf) return;
        var k = Difference.Class(a, b, rotateMod);
        if (k > 0 && k < classes.Length) classes[k]++;
    }
}
