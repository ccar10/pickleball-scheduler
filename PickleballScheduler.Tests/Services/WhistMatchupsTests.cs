using PickleballScheduler.Models;
using PickleballScheduler.Services;
using PickleballScheduler.Tests.Search;

namespace PickleballScheduler.Tests.Services;

public class WhistMatchupsTests
{
    public static TheoryData<int, int> ExpectedCoverageRounds()
    {
        // 0-indexed worst-case round at which the slowest player has shared a court with
        // everyone else. n=16 and n=20 updated 2026-05-13 when the search added the
        // foursome-uniqueness constraint (see WhistMatchups.cs comments). n=24 was
        // 20 (pre-redesign); reduced to 12 on 2026-05-30 via bounded search (the full
        // optimal search is intractable at n=24 with the current enumerator).
        var d = new TheoryData<int, int>();
        d.Add(8, 3);
        d.Add(12, 6);
        d.Add(16, 6);
        d.Add(20, 11);
        d.Add(24, 12);
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

    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    public void ShippedBaseRound_HasNoRepeatedFoursomesAcrossRotation(int n)
    {
        var candidate = ReconstructBaseRoundFromShipped(n);
        Assert.True(FoursomeUniquenessChecker.AllFoursomesUnique(candidate, out var reason),
            $"Wh({n}) foursome uniqueness failed: {reason}");
    }

    /// <summary>
    /// Foursome uniqueness rules out the *same* 4-player set across the whole schedule,
    /// but doesn't bound how many players from round r's foursome re-appear together in
    /// round r+1. Paul reported the worst case (3 of 4) for n=24 on 2026-05-19: Wh(24)
    /// match 0 was {inf, 0, 1, 2}, and the +1 rotation shifts that to {inf, 1, 2, 3} —
    /// three shared players on whatever court that foursome lands on, every round
    /// transition. The other Whist sizes already stay at ≤ 2 by base-round design.
    /// </summary>
    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    public void ShippedBaseRound_BackToBackSameCourtOverlap_AtMost2(int n)
    {
        var players = Enumerable.Range(1, n)
            .Select(i => new Player { Id = i, Name = $"P{i}" })
            .ToList();
        var generator = new ScheduleGenerator();
        var schedule = generator.Generate(players, numberOfCourts: n / 4, numberOfRounds: n - 1);

        int worst = 0;
        (int round, int court1, int court2)? worstAt = null;
        for (int r = 0; r < schedule.Rounds.Count - 1; r++)
        {
            var a = schedule.Rounds[r];
            var b = schedule.Rounds[r + 1];
            foreach (var ma in a.Matches)
            {
                var setA = new HashSet<int> { ma.Team1Player1Id, ma.Team1Player2Id, ma.Team2Player1Id, ma.Team2Player2Id };
                foreach (var mb in b.Matches)
                {
                    int overlap = (new[] { mb.Team1Player1Id, mb.Team1Player2Id, mb.Team2Player1Id, mb.Team2Player2Id })
                        .Count(setA.Contains);
                    if (overlap > worst)
                    {
                        worst = overlap;
                        worstAt = (r + 1, ma.CourtNumber, mb.CourtNumber);
                    }
                }
            }
        }

        Assert.True(worst <= 2,
            $"Wh({n}) has {worst}-player same-foursome overlap between consecutive rounds " +
            $"(round {worstAt?.round} court {worstAt?.court1} → round {worstAt?.round + 1} court {worstAt?.court2})");
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
