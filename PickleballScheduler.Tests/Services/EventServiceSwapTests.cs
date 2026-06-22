using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PickleballScheduler.Data;
using PickleballScheduler.Models;
using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

public class EventServiceSwapTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public EventServiceSwapTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();

        for (int i = 1; i <= 5; i++)
            db.Players.Add(new Player { Id = i, Name = $"P{i}" });

        var evt = new Event { Id = 1, Name = "E", Date = DateTime.Today, NumberOfCourts = 1, CourtNames = "" };
        db.Events.Add(evt);

        var round = new Round
        {
            Id = 1,
            EventId = 1,
            RoundNumber = 1,
            Matches = { new Match { CourtNumber = 1, Team1Player1Id = 1, Team1Player2Id = 2, Team2Player1Id = 3, Team2Player2Id = 4 } },
            Byes = { new Bye { PlayerId = 5 } },
        };
        db.Rounds.Add(round);
        db.SaveChanges();
    }

    [Fact]
    public async Task SwapPlayersAsync_CourtToBye_PersistsSwappedKey()
    {
        using (var db = new AppDbContext(_options))
        {
            var service = new EventService(db);
            await service.SwapPlayersAsync(roundId: 1, playerIdA: 3, playerIdB: 5);
        }

        using (var db = new AppDbContext(_options))
        {
            var round = db.Rounds.Include(r => r.Matches).Include(r => r.Byes).Single();
            var match = round.Matches.Single();
            Assert.Equal(5, match.Team2Player1Id); // bye player moved onto court
            Assert.Equal(3, round.Byes.Single().PlayerId); // court player now sits
        }
    }

    [Fact]
    public async Task SwapPlayersAsync_CourtToCourt_Persists()
    {
        using (var db = new AppDbContext(_options))
        {
            var service = new EventService(db);
            await service.SwapPlayersAsync(roundId: 1, playerIdA: 1, playerIdB: 4);
        }

        using (var db = new AppDbContext(_options))
        {
            var match = db.Rounds.Include(r => r.Matches).Single().Matches.Single();
            Assert.Equal(4, match.Team1Player1Id);
            Assert.Equal(1, match.Team2Player2Id);
        }
    }

    public void Dispose() => _connection.Dispose();
}
