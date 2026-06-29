using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PickleballScheduler.Data;
using PickleballScheduler.Models;

namespace PickleballScheduler.Tests.Models;

public class ScoreModelTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public ScoreModelTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
        for (int i = 1; i <= 4; i++) db.Players.Add(new Player { Id = i, Name = $"P{i}" });
        db.Events.Add(new Event { Id = 1, Name = "E", NumberOfCourts = 1, CourtNames = "" });
        db.SaveChanges();
    }

    [Fact]
    public void Match_RoundTripsNullableScores_AndRound_RoundTripsChampionshipFlag()
    {
        using (var db = new AppDbContext(_options))
        {
            db.Rounds.Add(new Round
            {
                Id = 1, EventId = 1, RoundNumber = 1, IsChampionship = true,
                Matches = { new Match { CourtNumber = 1, Team1Player1Id = 1, Team1Player2Id = 2,
                    Team2Player1Id = 3, Team2Player2Id = 4, Team1Score = 11, Team2Score = 7 } },
            });
            db.SaveChanges();
        }

        using (var db = new AppDbContext(_options))
        {
            var round = db.Rounds.Include(r => r.Matches).Single();
            Assert.True(round.IsChampionship);
            var match = round.Matches.Single();
            Assert.Equal(11, match.Team1Score);
            Assert.Equal(7, match.Team2Score);
        }
    }

    [Fact]
    public void Match_ScoresDefaultToNull_AndRound_IsChampionshipDefaultsFalse()
    {
        using (var db = new AppDbContext(_options))
        {
            db.Rounds.Add(new Round
            {
                Id = 2, EventId = 1, RoundNumber = 2,
                Matches = { new Match { CourtNumber = 1, Team1Player1Id = 1, Team1Player2Id = 2,
                    Team2Player1Id = 3, Team2Player2Id = 4 } },
            });
            db.SaveChanges();
        }

        using (var db = new AppDbContext(_options))
        {
            var round = db.Rounds.Include(r => r.Matches).Single(r => r.Id == 2);
            Assert.False(round.IsChampionship);
            var match = round.Matches.Single();
            Assert.Null(match.Team1Score);
            Assert.Null(match.Team2Score);
        }
    }

    public void Dispose() => _connection.Dispose();
}
