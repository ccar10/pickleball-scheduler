namespace PickleballScheduler.Tests.Search;

internal static class BaseRoundEnumerator
{
    /// <summary>
    /// Yields candidate base rounds for size n with symmetry-breaking applied and
    /// difference-class pruning to eliminate branches that can't produce valid Whist.
    /// Does NOT pre-filter for Whist validity — caller may still run WhistValidator
    /// as a sanity check.
    /// </summary>
    public static IEnumerable<BaseRoundCandidate> Enumerate(int n)
    {
        if (n % 4 != 0) throw new ArgumentException("n must be divisible by 4", nameof(n));
        if (!new[] { 8, 12, 16, 20, 24 }.Contains(n))
            throw new ArgumentException($"unsupported size {n}", nameof(n));

        int rotateMod = n - 1;
        int maxClass = rotateMod / 2;

        // partnerHits[k]  = how many finite partner pairs with diff-class k we've added so far.  Limit 1.
        // opponentHits[k] = how many finite opponent pairs with diff-class k we've added so far. Limit 2.
        var partnerHits  = new int[maxClass + 1];
        var opponentHits = new int[maxClass + 1];

        var finiteRoles = Enumerable.Range(0, rotateMod).ToArray();

        // Match[0] loop: (Inf, a, b, c).
        // Teams: (Inf, a) vs (b, c).
        //   Finite partner pair: (b, c)   — both finite.
        //   Finite opponent pairs: (a, b) and (a, c)  — Inf-side pairs are skipped.
        for (int a = 0; a < rotateMod; a++)
        for (int b = a + 1; b < rotateMod; b++)
        for (int c = b + 1; c < rotateMod; c++)
        {
            int bcClass = Difference.Class(b, c, rotateMod);  // partner
            int abClass = Difference.Class(a, b, rotateMod);  // opponent
            int acClass = Difference.Class(a, c, rotateMod);  // opponent

            // Apply m0 contributions; revert and skip if any class would be exceeded.
            if (!TryApply(partnerHits,  bcClass, 1)) continue;
            if (!TryApply(opponentHits, abClass, 2)) { Revert(partnerHits,  bcClass); continue; }
            if (!TryApply(opponentHits, acClass, 2)) { Revert(opponentHits, abClass); Revert(partnerHits,  bcClass); continue; }

            var m0 = new BaseRoundCandidate.Match(BaseRoundCandidate.Inf, a, b, c);
            var remaining = finiteRoles.Where(r => r != a && r != b && r != c).ToArray();

            foreach (var rest in PartitionIntoMatches(remaining, rotateMod, partnerHits, opponentHits))
            {
                var allMatches = new List<BaseRoundCandidate.Match> { m0 };
                allMatches.AddRange(rest);
                yield return new BaseRoundCandidate(n, allMatches);
            }

            // Revert m0 contributions.
            Revert(opponentHits, acClass);
            Revert(opponentHits, abClass);
            Revert(partnerHits,  bcClass);
        }
    }

    // Returns true and increments hits[k] if the result would not exceed limit; leaves array
    // unchanged and returns false if it would be exceeded.
    private static bool TryApply(int[] hits, int k, int limit)
    {
        if (k <= 0) return true;
        if (hits[k] >= limit) return false;
        ++hits[k];
        return true;
    }

    // Unconditionally decrements hits[k] (used only after a successful TryApply).
    private static void Revert(int[] hits, int k)
    {
        if (k > 0) --hits[k];
    }

    /// <summary>
    /// Partitions the remaining finite roles into matches of 4 with symmetry-breaking and
    /// difference-class pruning. Mutates partnerHits and opponentHits in-place, reverting
    /// on backtrack.
    /// </summary>
    private static IEnumerable<List<BaseRoundCandidate.Match>> PartitionIntoMatches(
        int[] roles, int rotateMod, int[] partnerHits, int[] opponentHits)
    {
        if (roles.Length == 0) { yield return new List<BaseRoundCandidate.Match>(); yield break; }
        if (roles.Length % 4 != 0) yield break;

        // Anchor: smallest remaining role is always team1A of the first remaining match.
        int anchor = roles[0];
        var pool = roles.Skip(1).ToArray();

        for (int i1B = 0; i1B < pool.Length; i1B++)
        for (int i2A = 0; i2A < pool.Length; i2A++)
        {
            if (i2A == i1B) continue;
            for (int i2B = i2A + 1; i2B < pool.Length; i2B++)
            {
                if (i2B == i1B) continue;
                int t1B = pool[i1B], t2A = pool[i2A], t2B = pool[i2B];

                // Symmetry-breaking: team1A < team1B, team1A < team2A.
                if (anchor >= t1B) continue;
                if (anchor >= t2A) continue;

                // Match (anchor, t1B, t2A, t2B):
                //   Teams: (anchor, t1B) vs (t2A, t2B).
                //   Partner pairs: (anchor, t1B) and (t2A, t2B).
                //   Opponent pairs: (anchor, t2A), (anchor, t2B), (t1B, t2A), (t1B, t2B).
                int p1Class = Difference.Class(anchor, t1B, rotateMod);
                int p2Class = Difference.Class(t2A,   t2B, rotateMod);
                int o1Class = Difference.Class(anchor, t2A, rotateMod);
                int o2Class = Difference.Class(anchor, t2B, rotateMod);
                int o3Class = Difference.Class(t1B,   t2A, rotateMod);
                int o4Class = Difference.Class(t1B,   t2B, rotateMod);

                // Tentatively apply each class; roll back all on first failure.
                if (!TryApply(partnerHits,  p1Class, 1)) continue;
                if (!TryApply(partnerHits,  p2Class, 1)) { Revert(partnerHits,  p1Class); continue; }
                if (!TryApply(opponentHits, o1Class, 2)) { Revert(partnerHits,  p2Class); Revert(partnerHits,  p1Class); continue; }
                if (!TryApply(opponentHits, o2Class, 2)) { Revert(opponentHits, o1Class); Revert(partnerHits,  p2Class); Revert(partnerHits,  p1Class); continue; }
                if (!TryApply(opponentHits, o3Class, 2)) { Revert(opponentHits, o2Class); Revert(opponentHits, o1Class); Revert(partnerHits,  p2Class); Revert(partnerHits,  p1Class); continue; }
                if (!TryApply(opponentHits, o4Class, 2)) { Revert(opponentHits, o3Class); Revert(opponentHits, o2Class); Revert(opponentHits, o1Class); Revert(partnerHits,  p2Class); Revert(partnerHits,  p1Class); continue; }

                // All constraints pass — recurse.
                var leftover = pool.Where((_, idx) => idx != i1B && idx != i2A && idx != i2B).ToArray();
                foreach (var rest in PartitionIntoMatches(leftover, rotateMod, partnerHits, opponentHits))
                {
                    var combined = new List<BaseRoundCandidate.Match>
                    {
                        new BaseRoundCandidate.Match(anchor, t1B, t2A, t2B)
                    };
                    combined.AddRange(rest);
                    yield return combined;
                }

                // Revert all applied increments on backtrack.
                Revert(opponentHits, o4Class);
                Revert(opponentHits, o3Class);
                Revert(opponentHits, o2Class);
                Revert(opponentHits, o1Class);
                Revert(partnerHits,  p2Class);
                Revert(partnerHits,  p1Class);
            }
        }
    }
}
