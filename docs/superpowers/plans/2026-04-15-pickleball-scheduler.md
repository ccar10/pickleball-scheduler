# Pickleball Round-Robin Scheduler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Blazor Server app that generates round-robin pickleball doubles schedules, tracks scores, and shows standings.

**Architecture:** Single .NET 8 Blazor Server project with EF Core + SQLite. Services handle business logic (scheduling algorithm, standings). Razor components handle UI with interactive server rendering. Bootstrap 5 for styling.

**Tech Stack:** .NET 8, Blazor Server, EF Core 8, SQLite, Bootstrap 5, xUnit + bUnit for testing.

---

## File Map

```
PickleballScheduler/
├── Data/
│   └── AppDbContext.cs                      # EF Core DbContext with all DbSets
├── Models/
│   ├── Player.cs                            # Player entity (Name, DuprRating)
│   ├── Event.cs                             # Event entity (Name, Date, Courts, SkillBalancing)
│   ├── EventPlayer.cs                       # Join table (EventId, PlayerId)
│   ├── Round.cs                             # Round entity (EventId, RoundNumber)
│   ├── Match.cs                             # Match entity (4 player FKs, scores)
│   └── Bye.cs                               # Bye entity (RoundId, PlayerId)
├── Services/
│   ├── PlayerService.cs                     # CRUD for player roster
│   ├── EventService.cs                      # CRUD for events, event-players, scores
│   ├── ScheduleGenerator.cs                 # Round-robin algorithm
│   └── StandingsService.cs                  # Win/loss/point-diff calculations
├── Components/
│   ├── Layout/
│   │   ├── MainLayout.razor                 # Nav bar + page shell
│   │   └── MainLayout.razor.css             # Scoped layout styles
│   └── Pages/
│       ├── Home.razor                       # Event list, create new event
│       ├── EventSetup.razor                 # Player selection, event config, generate
│       ├── Schedule.razor                   # Round/match view, inline score entry
│       └── Standings.razor                  # Leaderboard table
├── Components/App.razor                     # Root component (Bootstrap CSS link)
├── Components/Routes.razor                  # Router
├── Components/_Imports.razor                # Global usings
├── Program.cs                               # DI, middleware, DB migration on startup
├── appsettings.json                         # SQLite connection string
└── PickleballScheduler.csproj               # Package refs (EF Core, SQLite, etc.)

PickleballScheduler.Tests/
├── Services/
│   ├── ScheduleGeneratorTests.cs            # Algorithm correctness tests
│   └── StandingsServiceTests.cs             # Standings calculation tests
└── PickleballScheduler.Tests.csproj         # xUnit + EF Core InMemory
```

---

### Task 1: Scaffold Project and Configure EF Core + SQLite

**Files:**
- Create: `PickleballScheduler/PickleballScheduler.csproj`
- Create: `PickleballScheduler/Program.cs`
- Create: `PickleballScheduler/appsettings.json`
- Create: `PickleballScheduler/Components/App.razor`
- Create: `PickleballScheduler/Components/Routes.razor`
- Create: `PickleballScheduler/Components/_Imports.razor`
- Create: `PickleballScheduler/Components/Layout/MainLayout.razor`
- Create: `PickleballScheduler/Components/Layout/MainLayout.razor.css`

- [ ] **Step 1: Create the Blazor Server project**

Run:
```bash
cd C:/Users/ccaruso/Dropbox/round-robin
dotnet new blazor -n PickleballScheduler -int Server -f net8.0 --no-https -e
```

- [ ] **Step 2: Add EF Core + SQLite NuGet packages**

Run:
```bash
cd PickleballScheduler
dotnet add package Microsoft.EntityFrameworkCore.Sqlite --version 8.0.*
dotnet add package Microsoft.EntityFrameworkCore.Design --version 8.0.*
dotnet add package Microsoft.EntityFrameworkCore.Tools --version 8.0.*
```

- [ ] **Step 3: Add Bootstrap 5 CSS to App.razor**

Replace the contents of `PickleballScheduler/Components/App.razor`:

```html
<!DOCTYPE html>
<html lang="en">

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css" rel="stylesheet" />
    <link rel="stylesheet" href="app.css" />
    <link rel="stylesheet" href="PickleballScheduler.styles.css" />
    <HeadOutlet />
</head>

<body>
    <Routes />
    <script src="_framework/blazor.web.js"></script>
</body>

</html>
```

- [ ] **Step 4: Set up the nav bar in MainLayout.razor**

Replace the contents of `PickleballScheduler/Components/Layout/MainLayout.razor`:

```razor
@inherits LayoutComponentBase

<nav class="navbar navbar-dark bg-dark mb-4">
    <div class="container">
        <a class="navbar-brand" href="/">Pickleball Scheduler</a>
    </div>
</nav>

<div class="container">
    @Body
</div>

<div id="blazor-error-ui">
    An unhandled error has occurred.
    <a href="" class="reload">Reload</a>
    <a class="dismiss">🗙</a>
</div>
```

- [ ] **Step 5: Configure SQLite connection string in appsettings.json**

Replace `PickleballScheduler/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=Data/pickleball.db"
  }
}
```

- [ ] **Step 6: Verify the app builds and runs**

Run:
```bash
dotnet build
dotnet run --urls http://localhost:5100
```
Expected: App starts, navigating to `http://localhost:5100` shows a page with the "Pickleball Scheduler" nav bar.

Stop the app (Ctrl+C).

- [ ] **Step 7: Commit**

```bash
cd C:/Users/ccaruso/Dropbox/round-robin
git add PickleballScheduler/ .gitignore
git commit -m "feat: scaffold Blazor Server project with EF Core + SQLite"
```

---

### Task 2: Define Models and DbContext

**Files:**
- Create: `PickleballScheduler/Models/Player.cs`
- Create: `PickleballScheduler/Models/Event.cs`
- Create: `PickleballScheduler/Models/EventPlayer.cs`
- Create: `PickleballScheduler/Models/Round.cs`
- Create: `PickleballScheduler/Models/Match.cs`
- Create: `PickleballScheduler/Models/Bye.cs`
- Create: `PickleballScheduler/Data/AppDbContext.cs`
- Modify: `PickleballScheduler/Program.cs`

- [ ] **Step 1: Create Player model**

Create `PickleballScheduler/Models/Player.cs`:

```csharp
namespace PickleballScheduler.Models;

public class Player
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public decimal? DuprRating { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 2: Create Event model**

Create `PickleballScheduler/Models/Event.cs`:

```csharp
namespace PickleballScheduler.Models;

public class Event
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public bool UseSkillBalancing { get; set; }
    public int NumberOfCourts { get; set; } = 1;
    public List<EventPlayer> EventPlayers { get; set; } = new();
    public List<Round> Rounds { get; set; } = new();
}
```

- [ ] **Step 3: Create EventPlayer join model**

Create `PickleballScheduler/Models/EventPlayer.cs`:

```csharp
namespace PickleballScheduler.Models;

public class EventPlayer
{
    public int EventId { get; set; }
    public Event Event { get; set; } = null!;
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
}
```

- [ ] **Step 4: Create Round model**

Create `PickleballScheduler/Models/Round.cs`:

```csharp
namespace PickleballScheduler.Models;

public class Round
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event Event { get; set; } = null!;
    public int RoundNumber { get; set; }
    public List<Match> Matches { get; set; } = new();
    public List<Bye> Byes { get; set; } = new();
}
```

- [ ] **Step 5: Create Match model**

Create `PickleballScheduler/Models/Match.cs`:

```csharp
namespace PickleballScheduler.Models;

public class Match
{
    public int Id { get; set; }
    public int RoundId { get; set; }
    public Round Round { get; set; } = null!;
    public int CourtNumber { get; set; }
    public int Team1Player1Id { get; set; }
    public Player Team1Player1 { get; set; } = null!;
    public int Team1Player2Id { get; set; }
    public Player Team1Player2 { get; set; } = null!;
    public int Team2Player1Id { get; set; }
    public Player Team2Player1 { get; set; } = null!;
    public int Team2Player2Id { get; set; }
    public Player Team2Player2 { get; set; } = null!;
    public int? Team1Score { get; set; }
    public int? Team2Score { get; set; }
    public bool IsComplete { get; set; }
}
```

- [ ] **Step 6: Create Bye model**

Create `PickleballScheduler/Models/Bye.cs`:

```csharp
namespace PickleballScheduler.Models;

public class Bye
{
    public int RoundId { get; set; }
    public Round Round { get; set; } = null!;
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
}
```

- [ ] **Step 7: Create AppDbContext**

Create `PickleballScheduler/Data/AppDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PickleballScheduler.Models;

namespace PickleballScheduler.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Player> Players => Set<Player>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventPlayer> EventPlayers => Set<EventPlayer>();
    public DbSet<Round> Rounds => Set<Round>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Bye> Byes => Set<Bye>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventPlayer>()
            .HasKey(ep => new { ep.EventId, ep.PlayerId });

        modelBuilder.Entity<EventPlayer>()
            .HasOne(ep => ep.Event)
            .WithMany(e => e.EventPlayers)
            .HasForeignKey(ep => ep.EventId);

        modelBuilder.Entity<EventPlayer>()
            .HasOne(ep => ep.Player)
            .WithMany()
            .HasForeignKey(ep => ep.PlayerId);

        modelBuilder.Entity<Bye>()
            .HasKey(b => new { b.RoundId, b.PlayerId });

        modelBuilder.Entity<Bye>()
            .HasOne(b => b.Round)
            .WithMany(r => r.Byes)
            .HasForeignKey(b => b.RoundId);

        modelBuilder.Entity<Bye>()
            .HasOne(b => b.Player)
            .WithMany()
            .HasForeignKey(b => b.PlayerId);

        modelBuilder.Entity<Match>()
            .HasOne(m => m.Team1Player1).WithMany().HasForeignKey(m => m.Team1Player1Id).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Match>()
            .HasOne(m => m.Team1Player2).WithMany().HasForeignKey(m => m.Team1Player2Id).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Match>()
            .HasOne(m => m.Team2Player1).WithMany().HasForeignKey(m => m.Team2Player1Id).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Match>()
            .HasOne(m => m.Team2Player2).WithMany().HasForeignKey(m => m.Team2Player2Id).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 8: Register DbContext in Program.cs**

Replace `PickleballScheduler/Program.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PickleballScheduler.Components;
using PickleballScheduler.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();
```

- [ ] **Step 9: Create initial migration**

Run:
```bash
cd PickleballScheduler
dotnet ef migrations add InitialCreate
```
Expected: `Migrations/` directory created with migration files.

- [ ] **Step 10: Verify build**

Run:
```bash
dotnet build
```
Expected: Build succeeds with no errors.

- [ ] **Step 11: Commit**

```bash
cd C:/Users/ccaruso/Dropbox/round-robin
git add PickleballScheduler/
git commit -m "feat: add data models and EF Core DbContext with SQLite"
```

---

### Task 3: Build Player and Event Services

**Files:**
- Create: `PickleballScheduler/Services/PlayerService.cs`
- Create: `PickleballScheduler/Services/EventService.cs`
- Modify: `PickleballScheduler/Program.cs`

- [ ] **Step 1: Create PlayerService**

Create `PickleballScheduler/Services/PlayerService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PickleballScheduler.Data;
using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public class PlayerService
{
    private readonly AppDbContext _db;

    public PlayerService(AppDbContext db) => _db = db;

    public async Task<List<Player>> GetAllAsync()
        => await _db.Players.OrderBy(p => p.Name).ToListAsync();

    public async Task<Player> CreateAsync(string name, decimal? duprRating)
    {
        var player = new Player { Name = name, DuprRating = duprRating };
        _db.Players.Add(player);
        await _db.SaveChangesAsync();
        return player;
    }

    public async Task DeleteAsync(int id)
    {
        var player = await _db.Players.FindAsync(id);
        if (player != null)
        {
            _db.Players.Remove(player);
            await _db.SaveChangesAsync();
        }
    }

    public async Task UpdateAsync(Player player)
    {
        _db.Players.Update(player);
        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 2: Create EventService**

Create `PickleballScheduler/Services/EventService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PickleballScheduler.Data;
using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public class EventService
{
    private readonly AppDbContext _db;

    public EventService(AppDbContext db) => _db = db;

    public async Task<List<Event>> GetAllAsync()
        => await _db.Events
            .Include(e => e.EventPlayers)
            .OrderByDescending(e => e.Date)
            .ToListAsync();

    public async Task<Event?> GetByIdAsync(int id)
        => await _db.Events
            .Include(e => e.EventPlayers).ThenInclude(ep => ep.Player)
            .Include(e => e.Rounds).ThenInclude(r => r.Matches).ThenInclude(m => m.Team1Player1)
            .Include(e => e.Rounds).ThenInclude(r => r.Matches).ThenInclude(m => m.Team1Player2)
            .Include(e => e.Rounds).ThenInclude(r => r.Matches).ThenInclude(m => m.Team2Player1)
            .Include(e => e.Rounds).ThenInclude(r => r.Matches).ThenInclude(m => m.Team2Player2)
            .Include(e => e.Rounds).ThenInclude(r => r.Byes).ThenInclude(b => b.Player)
            .FirstOrDefaultAsync(e => e.Id == id);

    public async Task<Event> CreateAsync(string name, DateTime date, int courts, bool useSkillBalancing)
    {
        var evt = new Event
        {
            Name = name,
            Date = date,
            NumberOfCourts = courts,
            UseSkillBalancing = useSkillBalancing
        };
        _db.Events.Add(evt);
        await _db.SaveChangesAsync();
        return evt;
    }

    public async Task SetPlayersAsync(int eventId, List<int> playerIds)
    {
        var existing = await _db.EventPlayers.Where(ep => ep.EventId == eventId).ToListAsync();
        _db.EventPlayers.RemoveRange(existing);

        foreach (var pid in playerIds)
        {
            _db.EventPlayers.Add(new EventPlayer { EventId = eventId, PlayerId = pid });
        }
        await _db.SaveChangesAsync();
    }

    public async Task SaveScheduleAsync(int eventId, List<Round> rounds)
    {
        var existingRounds = await _db.Rounds
            .Where(r => r.EventId == eventId)
            .Include(r => r.Matches)
            .Include(r => r.Byes)
            .ToListAsync();

        foreach (var r in existingRounds)
        {
            _db.Matches.RemoveRange(r.Matches);
            _db.Byes.RemoveRange(r.Byes);
        }
        _db.Rounds.RemoveRange(existingRounds);

        foreach (var round in rounds)
        {
            round.EventId = eventId;
            _db.Rounds.Add(round);
        }
        await _db.SaveChangesAsync();
    }

    public async Task SaveMatchScoreAsync(int matchId, int team1Score, int team2Score)
    {
        var match = await _db.Matches.FindAsync(matchId);
        if (match != null)
        {
            match.Team1Score = team1Score;
            match.Team2Score = team2Score;
            match.IsComplete = true;
            await _db.SaveChangesAsync();
        }
    }

    public async Task DeleteAsync(int id)
    {
        var evt = await _db.Events
            .Include(e => e.Rounds).ThenInclude(r => r.Matches)
            .Include(e => e.Rounds).ThenInclude(r => r.Byes)
            .Include(e => e.EventPlayers)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (evt != null)
        {
            foreach (var r in evt.Rounds)
            {
                _db.Matches.RemoveRange(r.Matches);
                _db.Byes.RemoveRange(r.Byes);
            }
            _db.Rounds.RemoveRange(evt.Rounds);
            _db.EventPlayers.RemoveRange(evt.EventPlayers);
            _db.Events.Remove(evt);
            await _db.SaveChangesAsync();
        }
    }
}
```

- [ ] **Step 3: Register services in Program.cs**

Add these lines in `Program.cs` right after the `AddDbContext` call:

```csharp
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<EventService>();
```

Add the using at the top of `Program.cs`:

```csharp
using PickleballScheduler.Services;
```

- [ ] **Step 4: Verify build**

Run:
```bash
dotnet build
```
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
cd C:/Users/ccaruso/Dropbox/round-robin
git add PickleballScheduler/
git commit -m "feat: add PlayerService and EventService"
```

---

### Task 4: Build the Schedule Generator (TDD)

**Files:**
- Create: `PickleballScheduler/Services/ScheduleGenerator.cs`
- Create: `PickleballScheduler.Tests/PickleballScheduler.Tests.csproj`
- Create: `PickleballScheduler.Tests/Services/ScheduleGeneratorTests.cs`

- [ ] **Step 1: Create the test project**

Run:
```bash
cd C:/Users/ccaruso/Dropbox/round-robin
dotnet new xunit -n PickleballScheduler.Tests -f net8.0
cd PickleballScheduler.Tests
dotnet add reference ../PickleballScheduler/PickleballScheduler.csproj
```

- [ ] **Step 2: Write failing tests for the schedule generator**

Create `PickleballScheduler.Tests/Services/ScheduleGeneratorTests.cs`:

```csharp
using PickleballScheduler.Models;
using PickleballScheduler.Services;

namespace PickleballScheduler.Tests.Services;

public class ScheduleGeneratorTests
{
    private static List<Player> MakePlayers(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => new Player { Id = i, Name = $"Player {i}" })
            .ToList();
    }

    private static List<Player> MakeRatedPlayers(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => new Player { Id = i, Name = $"Player {i}", DuprRating = 2.0m + (i * 0.5m) })
            .ToList();
    }

    [Fact]
    public void Generate_4Players_ProducesRoundsWithUniquePartners()
    {
        var players = MakePlayers(4);
        var generator = new ScheduleGenerator();

        var rounds = generator.Generate(players, numberOfCourts: 1, numberOfRounds: 3, useSkillBalancing: false);

        Assert.Equal(3, rounds.Count);

        var partnerships = new HashSet<string>();
        foreach (var round in rounds)
        {
            Assert.Single(round.Matches);
            Assert.Empty(round.Byes);

            var match = round.Matches[0];
            var pair1 = PairKey(match.Team1Player1Id, match.Team1Player2Id);
            var pair2 = PairKey(match.Team2Player1Id, match.Team2Player2Id);

            Assert.DoesNotContain(pair1, partnerships);
            Assert.DoesNotContain(pair2, partnerships);
            partnerships.Add(pair1);
            partnerships.Add(pair2);
        }
    }

    [Fact]
    public void Generate_5Players_AssignsByesEvenly()
    {
        var players = MakePlayers(5);
        var generator = new ScheduleGenerator();

        var rounds = generator.Generate(players, numberOfCourts: 1, numberOfRounds: 4, useSkillBalancing: false);

        Assert.Equal(4, rounds.Count);

        var byeCounts = new Dictionary<int, int>();
        foreach (var round in rounds)
        {
            Assert.Single(round.Matches);
            Assert.Single(round.Byes);
            var byePlayerId = round.Byes[0].PlayerId;
            byeCounts[byePlayerId] = byeCounts.GetValueOrDefault(byePlayerId) + 1;
        }

        var maxByes = byeCounts.Values.Max();
        var minByes = byeCounts.Values.Min();
        Assert.True(maxByes - minByes <= 1, "Byes should be distributed evenly");
    }

    [Fact]
    public void Generate_8Players_2Courts_FillsBothCourts()
    {
        var players = MakePlayers(8);
        var generator = new ScheduleGenerator();

        var rounds = generator.Generate(players, numberOfCourts: 2, numberOfRounds: 3, useSkillBalancing: false);

        Assert.Equal(3, rounds.Count);
        foreach (var round in rounds)
        {
            Assert.Equal(2, round.Matches.Count);
            Assert.Empty(round.Byes);
        }
    }

    [Fact]
    public void Generate_NoRepeatedPartners()
    {
        var players = MakePlayers(8);
        var generator = new ScheduleGenerator();

        var rounds = generator.Generate(players, numberOfCourts: 2, numberOfRounds: 5, useSkillBalancing: false);

        var partnerships = new HashSet<string>();
        foreach (var round in rounds)
        {
            foreach (var match in round.Matches)
            {
                var pair1 = PairKey(match.Team1Player1Id, match.Team1Player2Id);
                var pair2 = PairKey(match.Team2Player1Id, match.Team2Player2Id);

                Assert.DoesNotContain(pair1, partnerships);
                Assert.DoesNotContain(pair2, partnerships);
                partnerships.Add(pair1);
                partnerships.Add(pair2);
            }
        }
    }

    [Fact]
    public void Generate_SkillBalancing_PairsHighWithLow()
    {
        var players = MakeRatedPlayers(4);
        var generator = new ScheduleGenerator();

        var rounds = generator.Generate(players, numberOfCourts: 1, numberOfRounds: 1, useSkillBalancing: true);

        var match = rounds[0].Matches[0];
        var team1Ratings = new[] {
            players.First(p => p.Id == match.Team1Player1Id).DuprRating!.Value,
            players.First(p => p.Id == match.Team1Player2Id).DuprRating!.Value
        };
        var team2Ratings = new[] {
            players.First(p => p.Id == match.Team2Player1Id).DuprRating!.Value,
            players.First(p => p.Id == match.Team2Player2Id).DuprRating!.Value
        };

        var team1Avg = team1Ratings.Average();
        var team2Avg = team2Ratings.Average();

        // With skill balancing, team averages should be close (within 1.0)
        Assert.True(Math.Abs(team1Avg - team2Avg) <= 1.0m,
            $"Team averages should be balanced: {team1Avg} vs {team2Avg}");
    }

    [Fact]
    public void Generate_6Players_1Court_2ByesPerRound()
    {
        var players = MakePlayers(6);
        var generator = new ScheduleGenerator();

        var rounds = generator.Generate(players, numberOfCourts: 1, numberOfRounds: 4, useSkillBalancing: false);

        foreach (var round in rounds)
        {
            Assert.Single(round.Matches);
            Assert.Equal(2, round.Byes.Count);
        }
    }

    [Fact]
    public void Generate_AllPlayersAppearInEveryRound()
    {
        var players = MakePlayers(8);
        var generator = new ScheduleGenerator();

        var rounds = generator.Generate(players, numberOfCourts: 2, numberOfRounds: 3, useSkillBalancing: false);

        foreach (var round in rounds)
        {
            var playerIds = new HashSet<int>();
            foreach (var match in round.Matches)
            {
                playerIds.Add(match.Team1Player1Id);
                playerIds.Add(match.Team1Player2Id);
                playerIds.Add(match.Team2Player1Id);
                playerIds.Add(match.Team2Player2Id);
            }
            foreach (var bye in round.Byes)
            {
                playerIds.Add(bye.PlayerId);
            }

            Assert.Equal(players.Count, playerIds.Count);
        }
    }

    private static string PairKey(int a, int b)
        => a < b ? $"{a}-{b}" : $"{b}-{a}";
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run:
```bash
cd C:/Users/ccaruso/Dropbox/round-robin/PickleballScheduler.Tests
dotnet test
```
Expected: All tests fail — `ScheduleGenerator` class doesn't exist yet.

- [ ] **Step 4: Implement ScheduleGenerator**

Create `PickleballScheduler/Services/ScheduleGenerator.cs`:

```csharp
using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public class ScheduleGenerator
{
    public List<Round> Generate(List<Player> players, int numberOfCourts, int numberOfRounds, bool useSkillBalancing)
    {
        var matchesPerRound = Math.Min(numberOfCourts, players.Count / 4);
        var playersPerRound = matchesPerRound * 4;
        var usedPartnerships = new HashSet<string>();
        var byeCounts = players.ToDictionary(p => p.Id, _ => 0);
        var rounds = new List<Round>();

        for (int r = 0; r < numberOfRounds; r++)
        {
            var activePlayers = SelectActivePlayers(players, playersPerRound, byeCounts);
            var byePlayers = players.Where(p => !activePlayers.Contains(p)).ToList();

            List<(Player, Player)> teams;
            if (useSkillBalancing)
            {
                teams = FormSkillBalancedTeams(activePlayers, usedPartnerships);
            }
            else
            {
                teams = FormTeams(activePlayers, usedPartnerships);
            }

            var matches = new List<Match>();
            for (int i = 0; i + 1 < teams.Count; i += 2)
            {
                matches.Add(new Match
                {
                    CourtNumber = (i / 2) + 1,
                    Team1Player1Id = teams[i].Item1.Id,
                    Team1Player2Id = teams[i].Item2.Id,
                    Team2Player1Id = teams[i + 1].Item1.Id,
                    Team2Player2Id = teams[i + 1].Item2.Id,
                });
            }

            foreach (var team in teams)
            {
                usedPartnerships.Add(PairKey(team.Item1.Id, team.Item2.Id));
            }

            foreach (var bp in byePlayers)
            {
                byeCounts[bp.Id]++;
            }

            rounds.Add(new Round
            {
                RoundNumber = r + 1,
                Matches = matches,
                Byes = byePlayers.Select(p => new Bye { PlayerId = p.Id }).ToList()
            });
        }

        return rounds;
    }

    private static List<Player> SelectActivePlayers(List<Player> players, int needed, Dictionary<int, int> byeCounts)
    {
        if (needed >= players.Count) return new List<Player>(players);

        return players
            .OrderByDescending(p => byeCounts[p.Id])
            .ThenBy(_ => Random.Shared.Next())
            .Take(needed)
            .ToList();
    }

    private static List<(Player, Player)> FormTeams(List<Player> activePlayers, HashSet<string> usedPartnerships)
    {
        var teams = new List<(Player, Player)>();
        var used = new HashSet<int>();
        var shuffled = activePlayers.OrderBy(_ => Random.Shared.Next()).ToList();

        for (int i = 0; i < shuffled.Count; i++)
        {
            if (used.Contains(shuffled[i].Id)) continue;

            for (int j = i + 1; j < shuffled.Count; j++)
            {
                if (used.Contains(shuffled[j].Id)) continue;

                var key = PairKey(shuffled[i].Id, shuffled[j].Id);
                if (!usedPartnerships.Contains(key))
                {
                    teams.Add((shuffled[i], shuffled[j]));
                    used.Add(shuffled[i].Id);
                    used.Add(shuffled[j].Id);
                    break;
                }
            }
        }

        // Fallback: if greedy couldn't pair everyone, pair remaining players even if partnership was used
        var unpaired = shuffled.Where(p => !used.Contains(p.Id)).ToList();
        for (int i = 0; i + 1 < unpaired.Count; i += 2)
        {
            teams.Add((unpaired[i], unpaired[i + 1]));
        }

        return teams;
    }

    private static List<(Player, Player)> FormSkillBalancedTeams(List<Player> activePlayers, HashSet<string> usedPartnerships)
    {
        var sorted = activePlayers
            .OrderByDescending(p => p.DuprRating ?? 3.0m)
            .ToList();

        var teams = new List<(Player, Player)>();
        var used = new HashSet<int>();

        int lo = 0, hi = sorted.Count - 1;
        while (lo < hi)
        {
            if (used.Contains(sorted[lo].Id)) { lo++; continue; }
            if (used.Contains(sorted[hi].Id)) { hi--; continue; }

            var key = PairKey(sorted[lo].Id, sorted[hi].Id);
            if (!usedPartnerships.Contains(key))
            {
                teams.Add((sorted[lo], sorted[hi]));
                used.Add(sorted[lo].Id);
                used.Add(sorted[hi].Id);
            }
            lo++;
            hi--;
        }

        // Fallback for remaining unpaired
        var unpaired = sorted.Where(p => !used.Contains(p.Id)).ToList();
        for (int i = 0; i + 1 < unpaired.Count; i += 2)
        {
            teams.Add((unpaired[i], unpaired[i + 1]));
        }

        return teams;
    }

    private static string PairKey(int a, int b)
        => a < b ? $"{a}-{b}" : $"{b}-{a}";
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run:
```bash
cd C:/Users/ccaruso/Dropbox/round-robin/PickleballScheduler.Tests
dotnet test
```
Expected: All 7 tests pass.

- [ ] **Step 6: Register ScheduleGenerator in Program.cs**

Add after the other service registrations:

```csharp
builder.Services.AddScoped<ScheduleGenerator>();
```

- [ ] **Step 7: Commit**

```bash
cd C:/Users/ccaruso/Dropbox/round-robin
git add PickleballScheduler/ PickleballScheduler.Tests/
git commit -m "feat: implement round-robin schedule generator with tests"
```

---

### Task 5: Build Standings Service (TDD)

**Files:**
- Create: `PickleballScheduler/Services/StandingsService.cs`
- Create: `PickleballScheduler.Tests/Services/StandingsServiceTests.cs`

- [ ] **Step 1: Write failing tests for standings**

Create `PickleballScheduler.Tests/Services/StandingsServiceTests.cs`:

```csharp
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
        // Carol: 1W 1L, -4 (-6+2)  -- wait let me recalc
        // Match 1: Team1(1,2) 11 vs Team2(3,4) 5 → diff=6. Alice +6, Bob +6, Carol -6, Dave -6
        // Match 2: Team1(1,3) 11 vs Team2(2,4) 9 → diff=2. Alice +2, Carol +2, Bob -2, Dave -2
        // Alice: 2W 0L, +8
        // Bob: 1W 1L, +4
        // Carol: 1W 1L, -4
        // Dave: 0W 2L, -8
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
```bash
cd C:/Users/ccaruso/Dropbox/round-robin/PickleballScheduler.Tests
dotnet test
```
Expected: Fails — `StandingsService` doesn't exist.

- [ ] **Step 3: Implement StandingsService**

Create `PickleballScheduler/Services/StandingsService.cs`:

```csharp
using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public class StandingsService
{
    public static List<PlayerStanding> Calculate(List<Player> players, List<Match> matches)
    {
        var stats = players.ToDictionary(p => p.Id, p => new PlayerStanding { Player = p });

        foreach (var match in matches.Where(m => m.IsComplete && m.Team1Score.HasValue && m.Team2Score.HasValue))
        {
            var t1Score = match.Team1Score!.Value;
            var t2Score = match.Team2Score!.Value;
            var diff = t1Score - t2Score;

            var team1Ids = new[] { match.Team1Player1Id, match.Team1Player2Id };
            var team2Ids = new[] { match.Team2Player1Id, match.Team2Player2Id };

            bool team1Won = t1Score > t2Score;

            foreach (var pid in team1Ids)
            {
                if (!stats.ContainsKey(pid)) continue;
                stats[pid].PointDifferential += diff;
                if (team1Won) stats[pid].Wins++; else stats[pid].Losses++;
            }
            foreach (var pid in team2Ids)
            {
                if (!stats.ContainsKey(pid)) continue;
                stats[pid].PointDifferential -= diff;
                if (!team1Won) stats[pid].Wins++; else stats[pid].Losses++;
            }
        }

        return stats.Values
            .OrderByDescending(s => s.Wins)
            .ThenByDescending(s => s.PointDifferential)
            .ToList();
    }
}

public class PlayerStanding
{
    public Player Player { get; set; } = null!;
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int PointDifferential { get; set; }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:
```bash
cd C:/Users/ccaruso/Dropbox/round-robin/PickleballScheduler.Tests
dotnet test
```
Expected: All tests pass (both ScheduleGenerator and Standings tests).

- [ ] **Step 5: Register StandingsService in Program.cs**

Add after other service registrations:

```csharp
builder.Services.AddScoped<StandingsService>();
```

- [ ] **Step 6: Commit**

```bash
cd C:/Users/ccaruso/Dropbox/round-robin
git add PickleballScheduler/ PickleballScheduler.Tests/
git commit -m "feat: implement standings service with win/loss/point-diff calculations"
```

---

### Task 6: Build Home Page (Event List)

**Files:**
- Modify: `PickleballScheduler/Components/Pages/Home.razor`
- Modify: `PickleballScheduler/Components/_Imports.razor`

- [ ] **Step 1: Update _Imports.razor with project namespaces**

Replace `PickleballScheduler/Components/_Imports.razor`:

```razor
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using static Microsoft.AspNetCore.Components.Web.RenderMode
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.JSInterop
@using PickleballScheduler
@using PickleballScheduler.Components
@using PickleballScheduler.Models
@using PickleballScheduler.Services
```

- [ ] **Step 2: Build the Home page**

Replace `PickleballScheduler/Components/Pages/Home.razor`:

```razor
@page "/"
@inject EventService EventService
@inject NavigationManager Nav
@rendermode InteractiveServer

<PageTitle>Pickleball Scheduler</PageTitle>

<div class="d-flex justify-content-between align-items-center mb-3">
    <h1>Events</h1>
    <a href="/event/new" class="btn btn-success">+ New Event</a>
</div>

@if (events == null)
{
    <p>Loading...</p>
}
else if (events.Count == 0)
{
    <div class="alert alert-info">No events yet. Create one to get started!</div>
}
else
{
    <div class="list-group">
        @foreach (var evt in events)
        {
            <a href="/event/@evt.Id/schedule" class="list-group-item list-group-item-action d-flex justify-content-between align-items-center">
                <div>
                    <strong>@evt.Name</strong>
                    <br />
                    <small class="text-muted">@evt.EventPlayers.Count players</small>
                </div>
                <span class="text-muted">@evt.Date.ToString("MMM d, yyyy")</span>
            </a>
        }
    </div>
}

@code {
    private List<Event>? events;

    protected override async Task OnInitializedAsync()
    {
        events = await EventService.GetAllAsync();
    }
}
```

- [ ] **Step 3: Verify build and manual test**

Run:
```bash
cd C:/Users/ccaruso/Dropbox/round-robin/PickleballScheduler
dotnet run --urls http://localhost:5100
```
Expected: Home page shows "No events yet" message and a green "New Event" button.

Stop the app.

- [ ] **Step 4: Commit**

```bash
cd C:/Users/ccaruso/Dropbox/round-robin
git add PickleballScheduler/
git commit -m "feat: add home page with event list"
```

---

### Task 7: Build Event Setup Page

**Files:**
- Create: `PickleballScheduler/Components/Pages/EventSetup.razor`

- [ ] **Step 1: Create the EventSetup page**

Create `PickleballScheduler/Components/Pages/EventSetup.razor`:

```razor
@page "/event/new"
@page "/event/{EventId:int}/setup"
@inject EventService EventService
@inject PlayerService PlayerService
@inject ScheduleGenerator ScheduleGenerator
@inject NavigationManager Nav
@rendermode InteractiveServer

<PageTitle>Event Setup</PageTitle>

<h1>@(isNew ? "New Event" : "Edit Event")</h1>

<div class="row">
    <div class="col-md-6">
        <div class="mb-3">
            <label class="form-label">Event Name</label>
            <input type="text" class="form-control" @bind="eventName" />
        </div>
        <div class="row mb-3">
            <div class="col">
                <label class="form-label">Date</label>
                <input type="date" class="form-control" @bind="eventDate" />
            </div>
            <div class="col">
                <label class="form-label">Courts</label>
                <input type="number" class="form-control" @bind="numberOfCourts" min="1" />
            </div>
        </div>
        <div class="form-check mb-3">
            <input type="checkbox" class="form-check-input" id="skillBalance" @bind="useSkillBalancing" />
            <label class="form-check-label" for="skillBalance">Use Skill Balancing (DUPR)</label>
        </div>
    </div>

    <div class="col-md-6">
        <h5>Players</h5>

        @if (allPlayers != null)
        {
            <div class="list-group mb-3" style="max-height: 300px; overflow-y: auto;">
                @foreach (var player in allPlayers)
                {
                    <label class="list-group-item d-flex justify-content-between align-items-center">
                        <div>
                            <input type="checkbox" class="form-check-input me-2"
                                   checked="@selectedPlayerIds.Contains(player.Id)"
                                   @onchange="() => TogglePlayer(player.Id)" />
                            @player.Name
                        </div>
                        <span class="text-muted">@(player.DuprRating?.ToString("F1") ?? "—")</span>
                    </label>
                }
            </div>
        }

        <div class="input-group mb-3">
            <input type="text" class="form-control" placeholder="New player name..." @bind="newPlayerName" />
            <input type="number" class="form-control" placeholder="DUPR" @bind="newPlayerRating" step="0.1" style="max-width: 100px;" />
            <button class="btn btn-outline-secondary" @onclick="AddPlayer">Add</button>
        </div>

        @if (!string.IsNullOrEmpty(validationError))
        {
            <div class="alert alert-danger">@validationError</div>
        }

        <div class="mb-3">
            <label class="form-label">Number of Rounds</label>
            <input type="number" class="form-control" @bind="numberOfRounds" min="1" />
            <small class="text-muted">Max before partner repeats: @(selectedPlayerIds.Count > 0 ? selectedPlayerIds.Count - 1 : 0)</small>
        </div>

        <button class="btn btn-success w-100" @onclick="GenerateSchedule" disabled="@isGenerating">
            @(isGenerating ? "Generating..." : "Generate Schedule")
        </button>
    </div>
</div>

@code {
    [Parameter] public int? EventId { get; set; }

    private bool isNew => EventId == null;
    private string eventName = "Pickleball Session";
    private DateTime eventDate = DateTime.Today;
    private int numberOfCourts = 2;
    private int numberOfRounds = 5;
    private bool useSkillBalancing;
    private List<Player>? allPlayers;
    private HashSet<int> selectedPlayerIds = new();
    private string newPlayerName = "";
    private decimal? newPlayerRating;
    private string? validationError;
    private bool isGenerating;

    protected override async Task OnInitializedAsync()
    {
        allPlayers = await PlayerService.GetAllAsync();

        if (EventId.HasValue)
        {
            var evt = await EventService.GetByIdAsync(EventId.Value);
            if (evt != null)
            {
                eventName = evt.Name;
                eventDate = evt.Date;
                numberOfCourts = evt.NumberOfCourts;
                useSkillBalancing = evt.UseSkillBalancing;
                selectedPlayerIds = evt.EventPlayers.Select(ep => ep.PlayerId).ToHashSet();
                numberOfRounds = evt.Rounds.Count > 0 ? evt.Rounds.Count : selectedPlayerIds.Count - 1;
            }
        }
    }

    private void TogglePlayer(int playerId)
    {
        if (!selectedPlayerIds.Remove(playerId))
        {
            selectedPlayerIds.Add(playerId);
        }
        numberOfRounds = Math.Min(numberOfRounds, Math.Max(1, selectedPlayerIds.Count - 1));
    }

    private async Task AddPlayer()
    {
        if (string.IsNullOrWhiteSpace(newPlayerName)) return;

        var player = await PlayerService.CreateAsync(newPlayerName.Trim(), newPlayerRating);
        allPlayers = await PlayerService.GetAllAsync();
        selectedPlayerIds.Add(player.Id);
        newPlayerName = "";
        newPlayerRating = null;
    }

    private async Task GenerateSchedule()
    {
        validationError = null;

        if (selectedPlayerIds.Count < 4)
        {
            validationError = "Please select at least 4 players.";
            return;
        }
        if (numberOfCourts < 1)
        {
            validationError = "Need at least 1 court.";
            return;
        }

        isGenerating = true;

        Event evt;
        if (isNew)
        {
            evt = await EventService.CreateAsync(eventName, eventDate, numberOfCourts, useSkillBalancing);
        }
        else
        {
            evt = (await EventService.GetByIdAsync(EventId!.Value))!;
            evt.Name = eventName;
            evt.Date = eventDate;
            evt.NumberOfCourts = numberOfCourts;
            evt.UseSkillBalancing = useSkillBalancing;
        }

        await EventService.SetPlayersAsync(evt.Id, selectedPlayerIds.ToList());

        var players = allPlayers!.Where(p => selectedPlayerIds.Contains(p.Id)).ToList();
        var rounds = ScheduleGenerator.Generate(players, numberOfCourts, numberOfRounds, useSkillBalancing);

        await EventService.SaveScheduleAsync(evt.Id, rounds);

        isGenerating = false;
        Nav.NavigateTo($"/event/{evt.Id}/schedule");
    }
}
```

- [ ] **Step 2: Verify build and manual test**

Run:
```bash
cd C:/Users/ccaruso/Dropbox/round-robin/PickleballScheduler
dotnet run --urls http://localhost:5100
```
Expected: Navigate to the home page, click "New Event", see the setup form. Add a few players, select them, and click Generate. Should redirect to the schedule page (which doesn't exist yet — you'll get a blank page, that's fine).

Stop the app.

- [ ] **Step 3: Commit**

```bash
cd C:/Users/ccaruso/Dropbox/round-robin
git add PickleballScheduler/
git commit -m "feat: add event setup page with player management and schedule generation"
```

---

### Task 8: Build Schedule View Page

**Files:**
- Create: `PickleballScheduler/Components/Pages/Schedule.razor`

- [ ] **Step 1: Create the Schedule page**

Create `PickleballScheduler/Components/Pages/Schedule.razor`:

```razor
@page "/event/{EventId:int}/schedule"
@inject EventService EventService
@inject NavigationManager Nav
@rendermode InteractiveServer

<PageTitle>Schedule</PageTitle>

@if (evt == null)
{
    <p>Loading...</p>
}
else
{
    <div class="d-flex justify-content-between align-items-center mb-3">
        <h1>@evt.Name</h1>
        <div>
            <a href="/event/@evt.Id/setup" class="btn btn-outline-secondary btn-sm me-2">Edit Setup</a>
            <a href="/event/@evt.Id/standings" class="btn btn-outline-primary btn-sm">Standings</a>
        </div>
    </div>
    <p class="text-muted">@evt.Date.ToString("MMMM d, yyyy") &middot; @evt.EventPlayers.Count players &middot; @evt.NumberOfCourts courts</p>

    @if (evt.Rounds.Count == 0)
    {
        <div class="alert alert-info">No schedule generated yet. <a href="/event/@evt.Id/setup">Go to setup</a> to generate one.</div>
    }
    else
    {
        @foreach (var round in evt.Rounds.OrderBy(r => r.RoundNumber))
        {
            <h4 class="mt-4 mb-3">Round @round.RoundNumber</h4>

            <div class="row">
                @foreach (var match in round.Matches.OrderBy(m => m.CourtNumber))
                {
                    <div class="col-md-6 mb-3">
                        <div class="card @(match.IsComplete ? "border-success" : "")">
                            <div class="card-header d-flex justify-content-between">
                                <span>Court @match.CourtNumber</span>
                                @if (match.IsComplete)
                                {
                                    <span class="text-success">&#10003;</span>
                                }
                            </div>
                            <div class="card-body">
                                <div class="d-flex justify-content-center align-items-center gap-3 mb-3">
                                    <div class="text-center">
                                        <strong>@match.Team1Player1.Name</strong>
                                        <br />
                                        <strong>@match.Team1Player2.Name</strong>
                                    </div>
                                    <span class="text-danger fw-bold">vs</span>
                                    <div class="text-center">
                                        <strong>@match.Team2Player1.Name</strong>
                                        <br />
                                        <strong>@match.Team2Player2.Name</strong>
                                    </div>
                                </div>
                                <div class="d-flex justify-content-center align-items-center gap-2">
                                    <input type="number" class="form-control form-control-sm text-center"
                                           style="max-width: 70px;"
                                           value="@match.Team1Score"
                                           @onchange="e => SaveScore(match.Id, e, true)" />
                                    <span>-</span>
                                    <input type="number" class="form-control form-control-sm text-center"
                                           style="max-width: 70px;"
                                           value="@match.Team2Score"
                                           @onchange="e => SaveScore(match.Id, e, false)" />
                                </div>
                            </div>
                        </div>
                    </div>
                }
            </div>

            @if (round.Byes.Any())
            {
                <p class="text-muted fst-italic text-center">
                    Sitting out: @string.Join(", ", round.Byes.Select(b => b.Player.Name))
                </p>
            }
        }
    }
}

@code {
    [Parameter] public int EventId { get; set; }

    private Event? evt;
    private Dictionary<int, int?> team1Scores = new();
    private Dictionary<int, int?> team2Scores = new();

    protected override async Task OnInitializedAsync()
    {
        evt = await EventService.GetByIdAsync(EventId);
        if (evt != null)
        {
            foreach (var round in evt.Rounds)
            {
                foreach (var match in round.Matches)
                {
                    team1Scores[match.Id] = match.Team1Score;
                    team2Scores[match.Id] = match.Team2Score;
                }
            }
        }
    }

    private async Task SaveScore(int matchId, ChangeEventArgs e, bool isTeam1)
    {
        if (!int.TryParse(e.Value?.ToString(), out var score)) return;

        if (isTeam1)
            team1Scores[matchId] = score;
        else
            team2Scores[matchId] = score;

        if (team1Scores.GetValueOrDefault(matchId).HasValue && team2Scores.GetValueOrDefault(matchId).HasValue)
        {
            await EventService.SaveMatchScoreAsync(matchId, team1Scores[matchId]!.Value, team2Scores[matchId]!.Value);
            evt = await EventService.GetByIdAsync(EventId);
        }
    }
}
```

- [ ] **Step 2: Verify build and manual test**

Run:
```bash
cd C:/Users/ccaruso/Dropbox/round-robin/PickleballScheduler
dotnet run --urls http://localhost:5100
```
Expected: Create a new event with players, generate schedule. The schedule page shows rounds with match cards, court numbers, team pairings, score inputs, and bye players listed. Entering both scores for a match should save and show a green checkmark.

Stop the app.

- [ ] **Step 3: Commit**

```bash
cd C:/Users/ccaruso/Dropbox/round-robin
git add PickleballScheduler/
git commit -m "feat: add schedule view page with inline score entry"
```

---

### Task 9: Build Standings Page

**Files:**
- Create: `PickleballScheduler/Components/Pages/Standings.razor`

- [ ] **Step 1: Create the Standings page**

Create `PickleballScheduler/Components/Pages/Standings.razor`:

```razor
@page "/event/{EventId:int}/standings"
@inject EventService EventService
@rendermode InteractiveServer

<PageTitle>Standings</PageTitle>

@if (evt == null)
{
    <p>Loading...</p>
}
else
{
    <div class="d-flex justify-content-between align-items-center mb-3">
        <h1>Standings</h1>
        <div>
            <a href="/event/@evt.Id/schedule" class="btn btn-outline-secondary btn-sm me-2">Schedule</a>
            <a href="/event/@evt.Id/setup" class="btn btn-outline-secondary btn-sm">Edit Setup</a>
        </div>
    </div>
    <p class="text-muted">@evt.Name &middot; @evt.Date.ToString("MMMM d, yyyy")</p>

    @if (standings == null || standings.Count == 0)
    {
        <div class="alert alert-info">No completed matches yet. Enter scores on the <a href="/event/@evt.Id/schedule">schedule page</a>.</div>
    }
    else
    {
        <table class="table table-striped">
            <thead>
                <tr>
                    <th>#</th>
                    <th>Player</th>
                    <th class="text-center">W</th>
                    <th class="text-center">L</th>
                    <th class="text-center">+/-</th>
                </tr>
            </thead>
            <tbody>
                @for (int i = 0; i < standings.Count; i++)
                {
                    var s = standings[i];
                    <tr>
                        <td>@(i + 1)</td>
                        <td>
                            @s.Player.Name
                            @if (s.Player.DuprRating.HasValue)
                            {
                                <small class="text-muted ms-2">(@s.Player.DuprRating.Value.ToString("F1"))</small>
                            }
                        </td>
                        <td class="text-center">@s.Wins</td>
                        <td class="text-center">@s.Losses</td>
                        <td class="text-center @(s.PointDifferential >= 0 ? "text-success" : "text-danger")">
                            @(s.PointDifferential >= 0 ? "+" : "")@s.PointDifferential
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    }
}

@code {
    [Parameter] public int EventId { get; set; }

    private Event? evt;
    private List<PlayerStanding>? standings;

    protected override async Task OnInitializedAsync()
    {
        evt = await EventService.GetByIdAsync(EventId);
        if (evt != null)
        {
            var players = evt.EventPlayers.Select(ep => ep.Player).ToList();
            var matches = evt.Rounds.SelectMany(r => r.Matches).ToList();
            standings = StandingsService.Calculate(players, matches);
        }
    }
}
```

- [ ] **Step 2: Verify build and full end-to-end test**

Run:
```bash
cd C:/Users/ccaruso/Dropbox/round-robin/PickleballScheduler
dotnet run --urls http://localhost:5100
```
Expected: Full flow works — create event, add players, generate schedule, enter scores, view standings. Standings page shows ranked table with W/L/+- colored green/red.

Stop the app.

- [ ] **Step 3: Commit**

```bash
cd C:/Users/ccaruso/Dropbox/round-robin
git add PickleballScheduler/
git commit -m "feat: add standings page with leaderboard"
```

---

### Task 10: Polish and Final Cleanup

**Files:**
- Modify: `PickleballScheduler/Components/Layout/MainLayout.razor`
- Modify: `PickleballScheduler/Components/Pages/Home.razor`
- Modify: `PickleballScheduler/wwwroot/app.css`

- [ ] **Step 1: Add event deletion to the Home page**

Add a delete button to each event on the Home page. In `Home.razor`, replace the `list-group` section:

```razor
    <div class="list-group">
        @foreach (var evt in events)
        {
            <div class="list-group-item d-flex justify-content-between align-items-center">
                <a href="/event/@evt.Id/schedule" class="text-decoration-none flex-grow-1">
                    <strong>@evt.Name</strong>
                    <br />
                    <small class="text-muted">@evt.EventPlayers.Count players</small>
                </a>
                <div class="d-flex align-items-center gap-3">
                    <span class="text-muted">@evt.Date.ToString("MMM d, yyyy")</span>
                    <button class="btn btn-outline-danger btn-sm" @onclick="() => DeleteEvent(evt.Id)">Delete</button>
                </div>
            </div>
        }
    </div>
```

Add delete method in the `@code` block:

```csharp
    private async Task DeleteEvent(int id)
    {
        await EventService.DeleteAsync(id);
        events = await EventService.GetAllAsync();
    }
```

- [ ] **Step 2: Add minimal custom CSS**

Replace `PickleballScheduler/wwwroot/app.css`:

```css
#blazor-error-ui {
    background: lightyellow;
    bottom: 0;
    box-shadow: 0 -1px 2px rgba(0, 0, 0, 0.2);
    display: none;
    left: 0;
    padding: 0.6rem 1.25rem 0.7rem 1.25rem;
    position: fixed;
    width: 100%;
    z-index: 1000;
}

#blazor-error-ui .dismiss {
    cursor: pointer;
    position: absolute;
    right: 0.75rem;
    top: 0.5rem;
}
```

- [ ] **Step 3: Run all tests**

Run:
```bash
cd C:/Users/ccaruso/Dropbox/round-robin/PickleballScheduler.Tests
dotnet test
```
Expected: All tests pass.

- [ ] **Step 4: Run the app and do a full manual walkthrough**

Run:
```bash
cd C:/Users/ccaruso/Dropbox/round-robin/PickleballScheduler
dotnet run --urls http://localhost:5100
```

Test the golden path:
1. Home page loads with no events
2. Click "New Event" — setup page loads
3. Add 6 players with names and optional DUPR ratings
4. Set 2 courts, check skill balancing, click Generate
5. Schedule page shows rounds with matches and byes
6. Enter scores for a few matches — green checkmarks appear
7. Click "Standings" — leaderboard shows correct W/L/+/-
8. Go back to Home — event appears in list
9. Click event — navigates to schedule
10. Delete event — removed from list

Stop the app.

- [ ] **Step 5: Commit**

```bash
cd C:/Users/ccaruso/Dropbox/round-robin
git add PickleballScheduler/
git commit -m "feat: add event deletion and polish UI"
```
