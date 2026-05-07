namespace PickleballScheduler.Tests.Search;

internal static class TestData
{
    /// <summary>
    /// Snapshot of the base rounds shipped in WhistMatchups before the redesign.
    /// Used to (a) verify the validator agrees they are valid Whist, and
    /// (b) compare baseline coverage before/after.
    /// </summary>
    public static BaseRoundCandidate ShippedBaseRound(int n) => n switch
    {
        8 => BaseRoundCandidate.FromTuples(8,
            (BaseRoundCandidate.Inf, 0, 1, 3),
            (2, 6, 4, 5)),
        12 => BaseRoundCandidate.FromTuples(12,
            (BaseRoundCandidate.Inf, 0, 1, 3),
            (2, 9, 6, 7),
            (4, 10, 5, 8)),
        16 => BaseRoundCandidate.FromTuples(16,
            (BaseRoundCandidate.Inf, 0, 1, 2),
            (3, 6, 9, 11),
            (4, 13, 8, 12),
            (5, 10, 7, 14)),
        20 => BaseRoundCandidate.FromTuples(20,
            (BaseRoundCandidate.Inf, 0, 1, 2),
            (3, 5, 9, 15),
            (4, 14, 17, 12),
            (6, 18, 10, 13),
            (7, 11, 16, 8)),
        24 => BaseRoundCandidate.FromTuples(24,
            (BaseRoundCandidate.Inf, 0, 1, 2),
            (3, 14, 4, 7),
            (5, 20, 12, 17),
            (6, 15, 21, 11),
            (8, 10, 13, 19),
            (9, 16, 18, 22)),
        _ => throw new ArgumentOutOfRangeException(nameof(n))
    };
}
