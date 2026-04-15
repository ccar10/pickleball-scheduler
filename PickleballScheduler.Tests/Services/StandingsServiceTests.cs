using PickleballScheduler.Models;
using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

public class StandingsServiceTests
{
    [Fact]
    public void Calculate_ReturnsCorrectWinsAndLosses()
    {
        var players = new List<Player>
        {
            new() { Id = 1, Name = "Alice" },
            new() { Id = 2, Name = "Bob" },
            new() { Id = 3, Name = "Carol" },
            new() { Id = 4, Name = "Dave" }
        };

        var matches = new List<Match>
        {
            new()
            {
                Team1Player1Id = 1, Team1Player2Id = 2,
                Team2Player1Id = 3, Team2Player2Id = 4,
                Team1Score = 11, Team2Score = 7, IsComplete = true
            }
        };

        var standings = StandingsService.Calculate(players, matches);

        var alice = standings.First(s => s.Player.Id == 1);
        Assert.Equal(1, alice.Wins);
        Assert.Equal(0, alice.Losses);

        var carol = standings.First(s => s.Player.Id == 3);
        Assert.Equal(0, carol.Wins);
        Assert.Equal(1, carol.Losses);
    }

    [Fact]
    public void Calculate_PointDifferentialIsCorrect()
    {
        var players = new List<Player>
        {
            new() { Id = 1, Name = "Alice" },
            new() { Id = 2, Name = "Bob" },
            new() { Id = 3, Name = "Carol" },
            new() { Id = 4, Name = "Dave" }
        };

        var matches = new List<Match>
        {
            new()
            {
                Team1Player1Id = 1, Team1Player2Id = 2,
                Team2Player1Id = 3, Team2Player2Id = 4,
                Team1Score = 11, Team2Score = 7, IsComplete = true
            },
            new()
            {
                Team1Player1Id = 1, Team1Player2Id = 3,
                Team2Player1Id = 2, Team2Player2Id = 4,
                Team1Score = 9, Team2Score = 11, IsComplete = true
            }
        };

        var standings = StandingsService.Calculate(players, matches);

        var alice = standings.First(s => s.Player.Id == 1);
        Assert.Equal(1, alice.Wins);
        Assert.Equal(1, alice.Losses);
        Assert.Equal(2, alice.PointDifferential); // (11-7) + (9-11) = 4 + (-2) = 2
    }

    [Fact]
    public void Calculate_SortedByWinsThenPointDiff()
    {
        var players = new List<Player>
        {
            new() { Id = 1, Name = "Alice" },
            new() { Id = 2, Name = "Bob" },
            new() { Id = 3, Name = "Carol" },
            new() { Id = 4, Name = "Dave" }
        };

        var matches = new List<Match>
        {
            new()
            {
                Team1Player1Id = 1, Team1Player2Id = 2,
                Team2Player1Id = 3, Team2Player2Id = 4,
                Team1Score = 11, Team2Score = 5, IsComplete = true
            },
            new()
            {
                Team1Player1Id = 1, Team1Player2Id = 3,
                Team2Player1Id = 2, Team2Player2Id = 4,
                Team1Score = 11, Team2Score = 9, IsComplete = true
            }
        };

        var standings = StandingsService.Calculate(players, matches);

        // Alice: 2W 0L, +8 (6+2)
        // Bob: 1W 1L, +4 (6-2)
        // Carol: 1W 1L, -4 (-6+2)
        // Dave: 0W 2L, -8 (-6-2)
        Assert.Equal("Alice", standings[0].Player.Name);
        Assert.Equal("Bob", standings[1].Player.Name);
        Assert.Equal("Carol", standings[2].Player.Name);
        Assert.Equal("Dave", standings[3].Player.Name);
    }

    [Fact]
    public void Calculate_IgnoresIncompleteMatches()
    {
        var players = new List<Player>
        {
            new() { Id = 1, Name = "Alice" },
            new() { Id = 2, Name = "Bob" },
            new() { Id = 3, Name = "Carol" },
            new() { Id = 4, Name = "Dave" }
        };

        var matches = new List<Match>
        {
            new()
            {
                Team1Player1Id = 1, Team1Player2Id = 2,
                Team2Player1Id = 3, Team2Player2Id = 4,
                Team1Score = null, Team2Score = null, IsComplete = false
            }
        };

        var standings = StandingsService.Calculate(players, matches);

        Assert.All(standings, s =>
        {
            Assert.Equal(0, s.Wins);
            Assert.Equal(0, s.Losses);
            Assert.Equal(0, s.PointDifferential);
        });
    }
}
