using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PickleballScheduler.Data;
using PickleballScheduler.Models;
using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

public class EventServiceChampionshipTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public EventServiceChampionshipTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
        for (int i = 1; i <= 8; i++)
        {
            db.Players.Add(new Player { Id = i, Name = $"P{i}" });
            db.EventPlayers.Add(new EventPlayer { EventId = 1, PlayerId = i });
        }
        db.Events.Add(new Event { Id = 1, Name = "E", NumberOfCourts = 2, CourtNames = "" });
        // One scored regular round so P1/P2 lead.
        db.Rounds.Add(new Round
        {
            Id = 1, EventId = 1, RoundNumber = 1,
            Matches =
            {
                new Match { CourtNumber = 1, Team1Player1Id = 1, Team1Player2Id = 2, Team2Player1Id = 3, Team2Player2Id = 4, Team1Score = 11, Team2Score = 2 },
                new Match { CourtNumber = 2, Team1Player1Id = 5, Team1Player2Id = 6, Team2Player1Id = 7, Team2Player2Id = 8, Team1Score = 11, Team2Score = 9 },
            },
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task SaveMatchScoreAsync_Persists()
    {
        int matchId;
        using (var db = new AppDbContext(_options))
            matchId = db.Matches.First(m => m.CourtNumber == 1).Id;

        using (var db = new AppDbContext(_options))
            await new EventService(db).SaveMatchScoreAsync(matchId, 8, 11);

        using (var db = new AppDbContext(_options))
        {
            var m = db.Matches.Single(x => x.Id == matchId);
            Assert.Equal(8, m.Team1Score);
            Assert.Equal(11, m.Team2Score);
        }
    }

    [Fact]
    public async Task GenerateChampionshipRoundAsync_CreatesSeededRound()
    {
        Round? created;
        using (var db = new AppDbContext(_options))
            created = await new EventService(db).GenerateChampionshipRoundAsync(1);

        Assert.NotNull(created);
        using (var db = new AppDbContext(_options))
        {
            var champ = db.Rounds.Include(r => r.Matches).Single(r => r.IsChampionship);
            Assert.Equal(2, champ.RoundNumber);
            Assert.Equal(2, champ.Matches.Count);
            // Winners P1,P2 (diff +9) outrank P5,P6 (diff +2); court 1 holds the top seeds.
            var court1Ids = champ.Matches.Single(m => m.CourtNumber == 1)
                .Let(m => new[] { m.Team1Player1Id, m.Team1Player2Id, m.Team2Player1Id, m.Team2Player2Id });
            Assert.Contains(1, court1Ids);
            Assert.Contains(2, court1Ids);
        }
    }

    [Fact]
    public async Task GenerateChampionshipRoundAsync_ReplacesExistingChampionshipRound()
    {
        using (var db = new AppDbContext(_options))
            await new EventService(db).GenerateChampionshipRoundAsync(1);
        using (var db = new AppDbContext(_options))
            await new EventService(db).GenerateChampionshipRoundAsync(1);

        using (var db = new AppDbContext(_options))
            Assert.Single(db.Rounds.Where(r => r.IsChampionship));
    }

    [Fact]
    public async Task RemoveChampionshipRoundAsync_DeletesIt()
    {
        using (var db = new AppDbContext(_options))
            await new EventService(db).GenerateChampionshipRoundAsync(1);
        using (var db = new AppDbContext(_options))
            await new EventService(db).RemoveChampionshipRoundAsync(1);

        using (var db = new AppDbContext(_options))
        {
            Assert.Empty(db.Rounds.Where(r => r.IsChampionship));
            Assert.Empty(db.Matches.Where(m => m.Round.IsChampionship));
        }
    }

    public void Dispose() => _connection.Dispose();
}

internal static class TestExtensions
{
    public static TResult Let<T, TResult>(this T self, Func<T, TResult> f) => f(self);
}
