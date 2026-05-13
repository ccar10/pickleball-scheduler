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
    /// </summary>
    public static bool AllFoursomesUnique(BaseRoundCandidate c, out string reason)
    {
        int rotateMod = c.RotateMod;
        var seen = new Dictionary<(int, int, int, int), (int round, int matchIndex)>();
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
                var key = (sorted[0], sorted[1], sorted[2], sorted[3]);

                if (seen.TryGetValue(key, out var prior))
                {
                    reason = $"foursome {{p{key.Item1},p{key.Item2},p{key.Item3},p{key.Item4}}} " +
                             $"appears in round {prior.round} match {prior.matchIndex} " +
                             $"and round {r} match {matchIndex}";
                    return false;
                }
                seen[key] = (r, matchIndex);
                matchIndex++;
            }
        }

        reason = "";
        return true;
    }

    private static int ResolvePlayer(int role, int round, int rotateMod)
    {
        if (role == BaseRoundCandidate.Inf) return 0;
        return ((role + round) % rotateMod) + 1;
    }
}
