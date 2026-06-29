using PickleballScheduler.Models;
using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

public class ChampionshipRoundBuilderTests
{
    // Standings already in rank order: ids 1..n are seeds 1..n.
    private static List<StandingsRow> Seeds(int n) =>
        Enumerable.Range(1, n)
            .Select(i => new StandingsRow(new Player { Id = i, Name = $"P{i}" }, 0, 0, 0))
            .ToList();

    [Fact]
    public void PairsBalanced_Seed1And4_VsSeed2And3()
    {
        var round = ChampionshipRoundBuilder.Build(Seeds(4), numberOfCourts: 1, nextRoundNumber: 5);

        Assert.NotNull(round);
        Assert.True(round!.IsChampionship);
        Assert.Equal(5, round.RoundNumber);
        var m = Assert.Single(round.Matches);
        Assert.Equal(1, m.CourtNumber);
        Assert.Equal(new[] { 1, 4 }, new[] { m.Team1Player1Id, m.Team1Player2Id });
        Assert.Equal(new[] { 2, 3 }, new[] { m.Team2Player1Id, m.Team2Player2Id });
        Assert.Empty(round.Byes);
    }

    [Fact]
    public void FillsCourtsTopDown_AndByesLowestSeeds()
    {
        // 14 players, 3 courts -> capacity = min(3, 3) = 3 courts (12 play), seeds 13-14 bye.
        var round = ChampionshipRoundBuilder.Build(Seeds(14), numberOfCourts: 3, nextRoundNumber: 1);

        Assert.NotNull(round);
        Assert.Equal(3, round!.Matches.Count);
        var court2 = round.Matches.Single(m => m.CourtNumber == 2);
        Assert.Equal(new[] { 5, 8 }, new[] { court2.Team1Player1Id, court2.Team1Player2Id });
        Assert.Equal(new[] { 6, 7 }, new[] { court2.Team2Player1Id, court2.Team2Player2Id });
        Assert.Equal(new[] { 13, 14 }, round.Byes.Select(b => b.PlayerId).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void CapacityLimitedByPlayerCount_NotJustCourts()
    {
        // 10 players, 4 courts -> floor(10/4)=2 courts play (8), seeds 9-10 bye.
        var round = ChampionshipRoundBuilder.Build(Seeds(10), numberOfCourts: 4, nextRoundNumber: 1);

        Assert.NotNull(round);
        Assert.Equal(2, round!.Matches.Count);
        Assert.Equal(new[] { 9, 10 }, round.Byes.Select(b => b.PlayerId).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void ReturnsNull_WhenFewerThanFourPlayers()
    {
        Assert.Null(ChampionshipRoundBuilder.Build(Seeds(3), numberOfCourts: 2, nextRoundNumber: 1));
    }
}
