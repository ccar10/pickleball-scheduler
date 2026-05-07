namespace PickleballScheduler.Tests.Search;

internal static class BaseRoundEnumerator
{
    /// <summary>
    /// Yields candidate base rounds for size n with symmetry-breaking applied.
    /// Does NOT pre-filter for Whist validity — caller must run WhistValidator.
    /// </summary>
    public static IEnumerable<BaseRoundCandidate> Enumerate(int n)
    {
        if (n % 4 != 0) throw new ArgumentException("n must be divisible by 4", nameof(n));
        if (!new[] { 8, 12, 16, 20, 24 }.Contains(n))
            throw new ArgumentException($"unsupported size {n}", nameof(n));

        int rotateMod = n - 1;

        // Pick 3 finite roles for match[0]; smallest becomes inf-partner (team1B).
        var finiteRoles = Enumerable.Range(0, rotateMod).ToArray();
        for (int a = 0; a < rotateMod; a++)
        for (int b = a + 1; b < rotateMod; b++)
        for (int c = b + 1; c < rotateMod; c++)
        {
            // Match[0]: inf, a (partner), then opponents b, c (sorted).
            var m0 = new BaseRoundCandidate.Match(BaseRoundCandidate.Inf, a, b, c);

            var remaining = finiteRoles.Where(r => r != a && r != b && r != c).ToArray();
            foreach (var rest in PartitionIntoMatches(remaining))
            {
                var allMatches = new List<BaseRoundCandidate.Match> { m0 };
                allMatches.AddRange(rest);
                yield return new BaseRoundCandidate(n, allMatches);
            }
        }
    }

    /// <summary>
    /// Partitions the remaining finite roles into matches of 4, then teams within each match,
    /// applying symmetry-breaking. Yields each valid partition once.
    /// </summary>
    private static IEnumerable<List<BaseRoundCandidate.Match>> PartitionIntoMatches(int[] roles)
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
                if (anchor >= t1B) continue;
                if (anchor >= t2A) continue;

                var match = new BaseRoundCandidate.Match(anchor, t1B, t2A, t2B);

                var leftover = pool.Where((_, idx) => idx != i1B && idx != i2A && idx != i2B).ToArray();
                foreach (var rest in PartitionIntoMatches(leftover))
                {
                    var combined = new List<BaseRoundCandidate.Match> { match };
                    combined.AddRange(rest);
                    yield return combined;
                }
            }
        }
    }
}
