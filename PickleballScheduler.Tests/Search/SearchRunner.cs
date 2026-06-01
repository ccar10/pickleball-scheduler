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
    ///
    /// Uses branch-and-bound on max-coverage and a fast bitmask uniqueness check.
    /// </summary>
    public static Best? FindBest(int n) => FindBest(n, progressPath: null);

    /// <summary>
    /// Sequential variant. Writes a progress line every 1,000 candidates evaluated to
    /// <paramref name="progressPath"/> (overwrites). Pass null to skip progress logging.
    /// </summary>
    public static Best? FindBest(int n, string? progressPath)
    {
        Best? best = null;
        long candidates = 0;
        long passedValidity = 0;
        long passedUniqueness = 0;
        var started = DateTime.UtcNow;
        const long progressInterval = 1_000;

        foreach (var candidate in BaseRoundEnumerator.Enumerate(n))
        {
            candidates++;
            if (progressPath != null && candidates % progressInterval == 0)
                WriteProgress(progressPath, false, started, candidates, passedValidity, passedUniqueness, best, 0, 0);

            if (!WhistValidator.IsValid(candidate, out _)) continue;
            passedValidity++;
            if (!FoursomeUniquenessChecker.AllFoursomesUniqueFast(candidate)) continue;
            passedUniqueness++;

            int cutoff = best?.MaxCoverageRound ?? int.MaxValue;
            var cov = CoverageCalculator.Compute(candidate, cutoff);
            if (cov.CoverageRoundByPlayer.Length == 0) continue;
            if (best == null
                || cov.MaxCoverageRound < best.MaxCoverageRound
                || (cov.MaxCoverageRound == best.MaxCoverageRound && cov.SumCoverageRound < best.SumCoverageRound))
            {
                best = new Best(candidate, cov.MaxCoverageRound, cov.SumCoverageRound);
            }
        }

        if (progressPath != null)
            WriteProgress(progressPath, true, started, candidates, passedValidity, passedUniqueness, best, 0, 0);
        return best;
    }

    /// <summary>
    /// Bounded parallel search: returns the FIRST candidate found with MaxCoverageRound ≤ target,
    /// then aborts all other workers. Use when the full optimal search is infeasible. Result is
    /// non-deterministic across runs (depends on thread scheduling) but is guaranteed to satisfy
    /// the target constraint when returned.
    /// </summary>
    public static Best? FindFirstAcceptable(int n, int targetMaxCoverage, string? progressPath)
    {
        var triples = BaseRoundEnumerator.EnumerateTopLevelTriples(n).ToList();
        Best? found = null;
        long totalCandidates = 0;
        long totalValidity = 0;
        long totalUniqueness = 0;
        long completedTriples = 0;
        var globalLock = new object();
        var started = DateTime.UtcNow;
        using var cts = new System.Threading.CancellationTokenSource();
        var po = new System.Threading.Tasks.ParallelOptions { CancellationToken = cts.Token };

        try
        {
            System.Threading.Tasks.Parallel.ForEach(triples, po, (triple, state) =>
            {
                long lc = 0, lv = 0, lu = 0;

                foreach (var candidate in BaseRoundEnumerator.EnumerateForTriple(n, triple.a, triple.b, triple.c))
                {
                    if (cts.IsCancellationRequested) return;
                    lc++;
                    if (!WhistValidator.IsValid(candidate, out _)) continue;
                    lv++;
                    if (!FoursomeUniquenessChecker.AllFoursomesUniqueFast(candidate)) continue;
                    lu++;

                    var cov = CoverageCalculator.Compute(candidate, targetMaxCoverage);
                    if (cov.CoverageRoundByPlayer.Length == 0) continue;  // pruned: exceeded target
                    if (cov.MaxCoverageRound > targetMaxCoverage) continue;

                    lock (globalLock)
                    {
                        if (found == null)
                        {
                            found = new Best(candidate, cov.MaxCoverageRound, cov.SumCoverageRound);
                            cts.Cancel();
                            if (progressPath != null)
                                WriteProgress(progressPath, true, started,
                                    totalCandidates + lc, totalValidity + lv, totalUniqueness + lu,
                                    found, completedTriples, triples.Count);
                        }
                    }
                    state.Stop();
                    return;
                }

                lock (globalLock)
                {
                    totalCandidates += lc;
                    totalValidity += lv;
                    totalUniqueness += lu;
                    completedTriples++;

                    if (progressPath != null && (completedTriples % 5 == 0 || completedTriples == triples.Count))
                        WriteProgress(progressPath, completedTriples == triples.Count, started,
                            totalCandidates, totalValidity, totalUniqueness, found, completedTriples, triples.Count);
                }
            });
        }
        catch (OperationCanceledException) { /* expected when found */ }

        return found;
    }

    /// <summary>
    /// Parallel variant. Splits work by top-level (a,b,c) match[0] triple across CPU cores.
    /// Maintains a shared "global best" used as a B&B cutoff floor by all worker threads,
    /// so a good candidate found in one thread prunes work in others. Result is identical
    /// to <see cref="FindBest(int, string?)"/> (same tiebreak rules).
    /// </summary>
    public static Best? FindBestParallel(int n, string? progressPath)
    {
        var triples = BaseRoundEnumerator.EnumerateTopLevelTriples(n).ToList();
        Best? globalBest = null;
        long totalCandidates = 0;
        long totalValidity = 0;
        long totalUniqueness = 0;
        long completedTriples = 0;
        var globalLock = new object();
        var started = DateTime.UtcNow;

        System.Threading.Tasks.Parallel.ForEach(triples, triple =>
        {
            Best? localBest = null;
            long lc = 0, lv = 0, lu = 0;

            foreach (var candidate in BaseRoundEnumerator.EnumerateForTriple(n, triple.a, triple.b, triple.c))
            {
                lc++;
                if (!WhistValidator.IsValid(candidate, out _)) continue;
                lv++;
                if (!FoursomeUniquenessChecker.AllFoursomesUniqueFast(candidate)) continue;
                lu++;

                int localCutoff = localBest?.MaxCoverageRound ?? int.MaxValue;
                // Volatile read — picks up updates from other threads without synchronization.
                var globalSnapshot = System.Threading.Volatile.Read(ref globalBest);
                int globalCutoff = globalSnapshot?.MaxCoverageRound ?? int.MaxValue;
                int cutoff = Math.Min(localCutoff, globalCutoff);

                var cov = CoverageCalculator.Compute(candidate, cutoff);
                if (cov.CoverageRoundByPlayer.Length == 0) continue;

                if (localBest == null
                    || cov.MaxCoverageRound < localBest.MaxCoverageRound
                    || (cov.MaxCoverageRound == localBest.MaxCoverageRound && cov.SumCoverageRound < localBest.SumCoverageRound))
                {
                    localBest = new Best(candidate, cov.MaxCoverageRound, cov.SumCoverageRound);
                }
            }

            lock (globalLock)
            {
                totalCandidates += lc;
                totalValidity += lv;
                totalUniqueness += lu;
                completedTriples++;

                if (localBest != null
                    && (globalBest == null
                        || localBest.MaxCoverageRound < globalBest.MaxCoverageRound
                        || (localBest.MaxCoverageRound == globalBest.MaxCoverageRound && localBest.SumCoverageRound < globalBest.SumCoverageRound)))
                {
                    globalBest = localBest;
                }

                if (progressPath != null && (completedTriples % 10 == 0 || completedTriples == triples.Count))
                    WriteProgress(progressPath, completedTriples == triples.Count, started,
                        totalCandidates, totalValidity, totalUniqueness, globalBest, completedTriples, triples.Count);
            }
        });

        return globalBest;
    }

    private static void WriteProgress(string path, bool done, DateTime started,
        long candidates, long validity, long uniqueness, Best? best,
        long completedTriples, long totalTriples)
    {
        var elapsed = DateTime.UtcNow - started;
        var bestStr = best == null ? "-" : best.MaxCoverageRound.ToString();
        var tripleStr = totalTriples > 0 ? $"triples={completedTriples}/{totalTriples} " : "";
        var prefix = done ? "DONE " : "";
        System.IO.File.WriteAllText(path,
            $"{prefix}elapsed={elapsed.TotalMinutes:F1}min {tripleStr}candidates={candidates:N0} " +
            $"validityPassed={validity:N0} uniquenessPassed={uniqueness:N0} bestMaxCoverage={bestStr}\n");
    }
}
