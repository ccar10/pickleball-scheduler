namespace PickleballScheduler.Tests.Search;

/// <summary>
/// Candidate Whist base round for size <see cref="PlayerCount"/>.
/// Match[0] always contains the inf role; remaining matches are finite-only.
/// Roles 0..PlayerCount-2 rotate; inf is fixed.
/// </summary>
internal sealed record BaseRoundCandidate(int PlayerCount, IReadOnlyList<BaseRoundCandidate.Match> Matches)
{
    public const int Inf = int.MinValue;

    public int RotateMod => PlayerCount - 1;

    public IEnumerable<int> FiniteRoles() =>
        Matches.SelectMany(m => new[] { m.Team1A, m.Team1B, m.Team2A, m.Team2B })
               .Where(r => r != Inf);

    public static BaseRoundCandidate FromTuples(int playerCount, params (int a, int b, int c, int d)[] matches)
    {
        var list = matches.Select(t => new Match(t.a, t.b, t.c, t.d)).ToList();
        return new BaseRoundCandidate(playerCount, list);
    }

    public sealed record Match(int Team1A, int Team1B, int Team2A, int Team2B);
}
