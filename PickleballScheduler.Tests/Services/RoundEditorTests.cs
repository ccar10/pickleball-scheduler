using PickleballScheduler.Models;
using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

public class RoundEditorTests
{
    // Round with two courts (8 players, ids 1..8) and no byes.
    private static Round TwoCourtRound() => new()
    {
        RoundNumber = 1,
        Matches = new List<Match>
        {
            new() { CourtNumber = 1, Team1Player1Id = 1, Team1Player2Id = 2, Team2Player1Id = 3, Team2Player2Id = 4 },
            new() { CourtNumber = 2, Team1Player1Id = 5, Team1Player2Id = 6, Team2Player1Id = 7, Team2Player2Id = 8 },
        },
    };

    // Round with one court (players 1..4) and a bye (player 5).
    private static Round CourtAndByeRound() => new()
    {
        RoundNumber = 1,
        Matches = new List<Match>
        {
            new() { CourtNumber = 1, Team1Player1Id = 1, Team1Player2Id = 2, Team2Player1Id = 3, Team2Player2Id = 4 },
        },
        Byes = new List<Bye> { new() { PlayerId = 5 } },
    };

    [Fact]
    public void SwapPlayers_CourtToCourt_ExchangesPositions()
    {
        var round = TwoCourtRound();

        RoundEditor.SwapPlayers(round, 1, 8);

        var court1 = round.Matches.Single(m => m.CourtNumber == 1);
        var court2 = round.Matches.Single(m => m.CourtNumber == 2);
        Assert.Equal(8, court1.Team1Player1Id);
        Assert.Equal(1, court2.Team2Player2Id);
        // Untouched positions stay put.
        Assert.Equal(2, court1.Team1Player2Id);
        Assert.Equal(7, court2.Team2Player1Id);
    }

    [Fact]
    public void SwapPlayers_CourtToBye_MovesByePlayerOntoCourt()
    {
        var round = CourtAndByeRound();

        RoundEditor.SwapPlayers(round, 3, 5);

        var court = round.Matches.Single();
        Assert.Equal(5, court.Team2Player1Id);   // bye player now plays
        Assert.Equal(3, round.Byes.Single().PlayerId); // court player now sits
    }

    [Fact]
    public void SwapPlayers_SamePlayer_IsNoOp()
    {
        var round = CourtAndByeRound();

        RoundEditor.SwapPlayers(round, 3, 3);

        var court = round.Matches.Single();
        Assert.Equal(3, court.Team2Player1Id);
        Assert.Equal(5, round.Byes.Single().PlayerId);
    }
}
