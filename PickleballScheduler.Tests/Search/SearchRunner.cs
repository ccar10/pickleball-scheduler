namespace PickleballScheduler.Tests.Search;

internal static class SearchRunner
{
    public sealed record Best(BaseRoundCandidate Candidate, int MaxCoverageRound, int SumCoverageRound);

    /// <summary>
    /// Enumerates all symmetry-broken candidates for size n, validates each, scores by max
    /// coverage round (tiebreak by sum). Returns the best, or null if no valid candidate found.
    ///
    /// Candidates must also satisfy <see cref="FoursomeUniquenessChecker"/>: across all n-1
    /// rotated rounds, every unordered 4-player foursome must appear at most once. Whist's
    /// pair-level invariants permit foursome repeats; rejecting them avoids the "round k+p
    /// has the same court groupings as round k" experience.
    /// </summary>
    public static Best? FindBest(int n)
    {
        Best? best = null;
        foreach (var candidate in BaseRoundEnumerator.Enumerate(n))
        {
            if (!WhistValidator.IsValid(candidate, out _)) continue;
            if (!FoursomeUniquenessChecker.AllFoursomesUnique(candidate, out _)) continue;
            var cov = CoverageCalculator.Compute(candidate);
            if (best == null
                || cov.MaxCoverageRound < best.MaxCoverageRound
                || (cov.MaxCoverageRound == best.MaxCoverageRound && cov.SumCoverageRound < best.SumCoverageRound))
            {
                best = new Best(candidate, cov.MaxCoverageRound, cov.SumCoverageRound);
            }
        }
        return best;
    }
}
