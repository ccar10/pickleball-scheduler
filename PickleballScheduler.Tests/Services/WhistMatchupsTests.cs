using PickleballScheduler.Models;
using PickleballScheduler.Services;
using PickleballScheduler.Tests.Search;

namespace PickleballScheduler.Tests.Services;

public class WhistMatchupsTests
{
    public static TheoryData<int, int> ExpectedCoverageRounds()
    {
        // 0-indexed worst-case round at which the slowest player has shared a court with
        // everyone else. Captured from the base-round search on 2026-05-07. n=24 is the
        // original value (search did not complete in budget).
        var d = new TheoryData<int, int>();
        d.Add(8, 3);
        d.Add(12, 6);
        d.Add(16, 4);
        d.Add(20, 10);
        d.Add(24, 20);
        return d;
    }

    [Theory]
    [MemberData(nameof(ExpectedCoverageRounds))]
    public void ShippedBaseRound_HasExpectedMaxCoverage(int n, int expectedMaxCoverageRound)
    {
        var candidate = ReconstructBaseRoundFromShipped(n);
        var result = CoverageCalculator.Compute(candidate);
        Assert.Equal(expectedMaxCoverageRound, result.MaxCoverageRound);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    public void ShippedBaseRound_PassesWhistValidator(int n)
    {
        var candidate = ReconstructBaseRoundFromShipped(n);
        Assert.True(WhistValidator.IsValid(candidate, out var reason),
            $"Wh({n}) validator failed: {reason}");
    }

    /// <summary>
    /// Reconstructs the production base round by calling WhistMatchups indirectly through
    /// the public ScheduleGenerator surface. Round 0's matches give us the base structure.
    /// Player IDs are 1..n (player 1 is inf in our convention here). Convert player IDs back
    /// to roles: role(playerId) = playerId == 1 ? Inf : playerId - 2.
    /// </summary>
    private static BaseRoundCandidate ReconstructBaseRoundFromShipped(int n)
    {
        var players = Enumerable.Range(1, n)
            .Select(i => new Player { Id = i, Name = $"P{i}" })
            .ToList();
        var generator = new ScheduleGenerator();
        var schedule = generator.Generate(players, numberOfCourts: n / 4, numberOfRounds: 1);
        var matches = schedule.Rounds[0].Matches;

        var asTuples = matches.Select(m => (
            RoleFromPlayerId(m.Team1Player1Id),
            RoleFromPlayerId(m.Team1Player2Id),
            RoleFromPlayerId(m.Team2Player1Id),
            RoleFromPlayerId(m.Team2Player2Id)
        )).ToArray();
        return BaseRoundCandidate.FromTuples(n, asTuples);
    }

    private static int RoleFromPlayerId(int pid) =>
        pid == 1 ? BaseRoundCandidate.Inf : pid - 2;
}
