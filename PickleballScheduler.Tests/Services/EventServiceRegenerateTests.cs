using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PickleballScheduler.Data;
using PickleballScheduler.Models;
using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

// Reproduces the Regenerate flow as it happens in Blazor Server: a single
// circuit-scoped DbContext loads the event graph (tracked), then saves a new
// schedule, then reloads — all on the SAME context instance.
public class EventServiceRegenerateTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public EventServiceRegenerateTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
        for (int i = 1; i <= 8; i++)
            db.Players.Add(new Player { Id = i, Name = $"P{i}" });
        db.Events.Add(new Event { Id = 4, Name = "E", Date = DateTime.Today, NumberOfCourts = 2, CourtNames = "" });
        for (int i = 1; i <= 8; i++)
            db.EventPlayers.Add(new EventPlayer { EventId = 4, PlayerId = i });
        db.SaveChanges();

        // Seed an initial 4-round schedule.
        using var seedCtx = new AppDbContext(_options);
        var players = Enumerable.Range(1, 8).Select(i => new Player { Id = i, Name = $"P{i}" }).ToList();
        var initial = new ScheduleGenerator().Generate(players, numberOfCourts: 2, numberOfRounds: 4);
        new EventService(seedCtx).SaveScheduleAsync(4, initial).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Regenerate_ExactComponentFlow_OnSameContext_ReplacesSchedule()
    {
        // One long-lived context, exactly like a Blazor Server circuit.
        using var ctx = new AppDbContext(_options);
        var service = new EventService(ctx);
        var generator = new ScheduleGenerator();

        // OnInitializedAsync: load & track the whole event graph.
        var evt = await service.GetByIdAsync(4);
        Assert.NotNull(evt);

        var firstRoundBefore = Signature(evt!);

        // ---- Verbatim copy of Schedule.razor's Regenerate() body ----
        var rng = Random.Shared;
        var shuffled = evt.EventPlayers
            .Select(ep => ep.Player!)
            .OrderBy(_ => rng.Next())
            .ToList();
        var rounds = evt.Rounds.Count > 0 ? evt.Rounds.Count : 1;

        var result = generator.Generate(shuffled, evt.NumberOfCourts, rounds);
        await service.SaveScheduleAsync(evt.Id, result);

        evt = await service.GetByIdAsync(4);
        // -------------------------------------------------------------

        Assert.NotNull(evt);
        // The reloaded schedule must reflect what we just saved, not stale
        // tracked data and not the pre-regenerate schedule.
        var savedSignature = string.Join("|",
            result.Rounds.OrderBy(r => r.RoundNumber)
                .SelectMany(r => r.Matches.OrderBy(m => m.CourtNumber)
                    .Select(m => $"{m.Team1Player1Id},{m.Team1Player2Id},{m.Team2Player1Id},{m.Team2Player2Id}")));

        Assert.Equal(savedSignature, Signature(evt!));
    }

    private static string Signature(Event evt) => string.Join("|",
        evt.Rounds.OrderBy(r => r.RoundNumber)
            .SelectMany(r => r.Matches.OrderBy(m => m.CourtNumber)
                .Select(m => $"{m.Team1Player1Id},{m.Team1Player2Id},{m.Team2Player1Id},{m.Team2Player2Id}")));

    public void Dispose() => _connection.Dispose();
}
