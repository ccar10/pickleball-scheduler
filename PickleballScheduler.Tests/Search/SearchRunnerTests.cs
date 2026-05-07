using Xunit.Abstractions;

namespace PickleballScheduler.Tests.Search;

public class SearchRunnerTests
{
    private readonly ITestOutputHelper _output;
    public SearchRunnerTests(ITestOutputHelper output) { _output = output; }

    [Fact]
    public void Find_n8_ReturnsValidCandidateNoWorseThanShipped()
    {
        var shipped = TestData.ShippedBaseRound(8);
        var shippedCoverage = CoverageCalculator.Compute(shipped).MaxCoverageRound;

        var best = SearchRunner.FindBest(8);
        Assert.NotNull(best);

        Assert.True(WhistValidator.IsValid(best!.Candidate, out _));
        Assert.True(best.MaxCoverageRound <= shippedCoverage,
            $"search found {best.MaxCoverageRound}, expected <= shipped {shippedCoverage}");

        _output.WriteLine($"Wh(8) best max-coverage round (0-indexed): {best.MaxCoverageRound}");
        _output.WriteLine(FormatForPaste(best.Candidate));
    }

    private static string FormatForPaste(BaseRoundCandidate c)
    {
        var lines = c.Matches.Select(m =>
        {
            string F(int r) => r == BaseRoundCandidate.Inf ? "\"inf\"" : $"\"{r}\"";
            return $"    new BaseMatch({F(m.Team1A)}, {F(m.Team1B)}, {F(m.Team2A)}, {F(m.Team2B)}),";
        });
        return $"[{c.PlayerCount}] = new[]\n{{\n{string.Join("\n", lines)}\n}},";
    }

    [Theory(Skip = "Manual run: unskip to regenerate base rounds. Can take minutes for large n.")]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    public void Find_n_PrintsBestForPaste(int n)
    {
        var shipped = TestData.ShippedBaseRound(n);
        var shippedCoverage = CoverageCalculator.Compute(shipped).MaxCoverageRound;

        var best = SearchRunner.FindBest(n);
        Assert.NotNull(best);

        Assert.True(WhistValidator.IsValid(best!.Candidate, out _));
        _output.WriteLine($"Wh({n}) best max-coverage round (0-indexed): {best.MaxCoverageRound}");
        _output.WriteLine($"Wh({n}) shipped max-coverage round (0-indexed): {shippedCoverage}");
        _output.WriteLine($"Improvement: {shippedCoverage - best.MaxCoverageRound} rounds");
        _output.WriteLine("");
        _output.WriteLine(FormatForPaste(best.Candidate));
    }
}
