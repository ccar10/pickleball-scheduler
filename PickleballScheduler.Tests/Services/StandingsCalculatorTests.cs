using PickleballScheduler.Models;
using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

public class StandingsCalculatorTests
{
    private static Player P(int id) => new() { Id = id, Name = $"P{id}" };

    private static Match M(int c, int t1p1, int t1p2, int t2p1, int t2p2, int? s1, int? s2) => new()
    {
        CourtNumber = c, Team1Player1Id = t1p1, Team1Player2Id = t1p2,
        Team2Player1Id = t2p1, Team2Player2Id = t2p2, Team1Score = s1, Team2Score = s2,
    };

    [Fact]
    public void WinnerGetsWin_BothTeamsGetPoints()
    {
        var players = new[] { P(1), P(2), P(3), P(4) };
        var rounds = new[] { new Round { RoundNumber = 1, Matches = { M(1, 1, 2, 3, 4, 11, 7) } } };

        var rows = StandingsCalculator.Compute(players, rounds);

        var p1 = rows.Single(r => r.Player.Id == 1);
        Assert.Equal(1, p1.Wins);
        Assert.Equal(11, p1.PointsFor);
        Assert.Equal(7, p1.PointsAgainst);
        Assert.Equal(4, p1.Diff);
        var p3 = rows.Single(r => r.Player.Id == 3);
        Assert.Equal(0, p3.Wins);
        Assert.Equal(-4, p3.Diff);
    }

    [Fact]
    public void TieAwardsNoWin_ButCountsPoints()
    {
        var players = new[] { P(1), P(2), P(3), P(4) };
        var rounds = new[] { new Round { RoundNumber = 1, Matches = { M(1, 1, 2, 3, 4, 9, 9) } } };

        var rows = StandingsCalculator.Compute(players, rounds);

        Assert.All(rows, r => Assert.Equal(0, r.Wins));
        Assert.Equal(9, rows.Single(r => r.Player.Id == 1).PointsFor);
        Assert.Equal(0, rows.Single(r => r.Player.Id == 1).Diff);
    }

    [Fact]
    public void UnscoredMatchesAndChampionshipRoundAreIgnored()
    {
        var players = new[] { P(1), P(2), P(3), P(4) };
        var rounds = new[]
        {
            new Round { RoundNumber = 1, Matches = { M(1, 1, 2, 3, 4, null, null) } },     // unscored
            new Round { RoundNumber = 2, Matches = { M(1, 1, 2, 3, 4, 11, 0) } },           // only T1 scored
            new Round { RoundNumber = 3, IsChampionship = true, Matches = { M(1, 1, 2, 3, 4, 11, 0) } },
        };
        // Make round 2 partially scored (T2 null) to verify "both required".
        rounds[1].Matches[0].Team2Score = null;

        var rows = StandingsCalculator.Compute(players, rounds);

        Assert.All(rows, r => { Assert.Equal(0, r.Wins); Assert.Equal(0, r.PointsFor); });
    }

    [Fact]
    public void SortsByWinsThenDiffThenPointsForThenName()
    {
        var players = new[] { P(1), P(2), P(3), P(4) };
        var rounds = new[]
        {
            new Round { RoundNumber = 1, Matches = { M(1, 1, 3, 2, 4, 11, 5) } },   // 1,3 win
            new Round { RoundNumber = 2, Matches = { M(1, 1, 4, 2, 3, 11, 9) } },   // 1,4 win
        };
        // Wins: P1=2, P3=1, P4=1, P2=0. P3 diff=+6+(-2)=+4 vs P4 diff=-6+2=-4 -> P3 above P4.
        var rows = StandingsCalculator.Compute(players, rounds);

        Assert.Equal(new[] { 1, 3, 4, 2 }, rows.Select(r => r.Player.Id).ToArray());
    }

    [Fact]
    public void EveryPlayerGetsARow_EvenWithNoScoredMatches()
    {
        var players = new[] { P(1), P(2), P(3), P(4), P(5) };
        var rounds = new[] { new Round { RoundNumber = 1, Matches = { M(1, 1, 2, 3, 4, 11, 7) },
            Byes = { new Bye { PlayerId = 5 } } } };

        var rows = StandingsCalculator.Compute(players, rounds);

        Assert.Equal(5, rows.Count);
        Assert.Contains(rows, r => r.Player.Id == 5 && r.Wins == 0 && r.PointsFor == 0);
    }
}
