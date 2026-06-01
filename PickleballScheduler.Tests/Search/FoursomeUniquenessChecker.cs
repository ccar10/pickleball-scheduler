namespace PickleballScheduler.Tests.Search;

internal static class FoursomeUniquenessChecker
{
    /// <summary>
    /// Returns true iff across all n-1 rotated rounds, every unordered 4-player foursome
    /// (the set of 4 players sharing a court, ignoring partnerships) appears at most once.
    ///
    /// A base round whose match-role multiset has a non-trivial stabilizer under cyclic
    /// shift mod (n-1) will fail this check: even though partnerships remain distinct
    /// across all n-1 rounds, the unordered foursomes repeat with period equal to the
    /// stabilizer's smallest non-zero element.
    ///
    /// Uses a 20-bit packed key (5 bits per sorted player ID, n <= 32) and a HashSet<uint>
    /// for the duplicate check. Collisions are reconstructed for the reason string.
    /// </summary>
    public static bool AllFoursomesUnique(BaseRoundCandidate c, out string reason)
    {
        int rotateMod = c.RotateMod;
        if (c.PlayerCount > 32)
            throw new InvalidOperationException("Packed-key check requires n <= 32");

        var seen = new HashSet<uint>();
        Span<int> sorted = stackalloc int[4];

        for (int r = 0; r < rotateMod; r++)
        {
            int matchIndex = 0;
            foreach (var m in c.Matches)
            {
                sorted[0] = ResolvePlayer(m.Team1A, r, rotateMod);
                sorted[1] = ResolvePlayer(m.Team1B, r, rotateMod);
                sorted[2] = ResolvePlayer(m.Team2A, r, rotateMod);
                sorted[3] = ResolvePlayer(m.Team2B, r, rotateMod);
                sorted.Sort();

                uint key = (uint)sorted[0]
                         | ((uint)sorted[1] << 5)
                         | ((uint)sorted[2] << 10)
                         | ((uint)sorted[3] << 15);

                if (!seen.Add(key))
                {
                    reason = $"foursome {{p{sorted[0]},p{sorted[1]},p{sorted[2]},p{sorted[3]}}} " +
                             $"appears at round {r} match {matchIndex} and earlier";
                    return false;
                }
                matchIndex++;
            }
        }

        reason = "";
        return true;
    }

    /// <summary>
    /// Bool-only variant for hot-path search use. Same semantics as <see cref="AllFoursomesUnique"/>
    /// without the reason-string construction.
    /// </summary>
    public static bool AllFoursomesUniqueFast(BaseRoundCandidate c)
    {
        int rotateMod = c.RotateMod;
        var seen = new HashSet<uint>();
        Span<int> sorted = stackalloc int[4];

        for (int r = 0; r < rotateMod; r++)
        {
            foreach (var m in c.Matches)
            {
                sorted[0] = ResolvePlayer(m.Team1A, r, rotateMod);
                sorted[1] = ResolvePlayer(m.Team1B, r, rotateMod);
                sorted[2] = ResolvePlayer(m.Team2A, r, rotateMod);
                sorted[3] = ResolvePlayer(m.Team2B, r, rotateMod);
                sorted.Sort();

                uint key = (uint)sorted[0]
                         | ((uint)sorted[1] << 5)
                         | ((uint)sorted[2] << 10)
                         | ((uint)sorted[3] << 15);

                if (!seen.Add(key)) return false;
            }
        }
        return true;
    }

    private static int ResolvePlayer(int role, int round, int rotateMod)
    {
        if (role == BaseRoundCandidate.Inf) return 0;
        return ((role + round) % rotateMod) + 1;
    }
}
