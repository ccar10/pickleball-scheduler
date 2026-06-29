# Championship Round Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let organizers optionally record match scores, see a live standings leaderboard, and generate a final "championship" round that seeds the strongest players onto the top court, the next group onto the next court, and so on.

**Architecture:** Two new persisted fields (`Match.Team1Score`/`Team2Score`, `Round.IsChampionship`) via an EF Core migration. A pure `StandingsCalculator` computes per-player rankings from scored regular-round matches. A pure `ChampionshipRoundBuilder` turns ranked standings into a seeded `Round`. `EventService` gains persistence methods, and `Schedule.razor` gains inline score inputs, a printable standings table, and Generate/Remove buttons.

**Tech Stack:** .NET 8, Blazor Server (Interactive Server render mode), EF Core + SQLite, xUnit tests against in-memory SQLite (`DataSource=:memory:` + `EnsureCreated()`).

## Global Constraints

- Target framework: **.NET 8**. Match existing C# style (file-scoped namespaces, nullable reference types enabled, `null!` for EF navigations).
- Scores are **nullable non-negative integers**; `null` = unscored. A match counts toward standings only when **both** scores are non-null.
- The championship round is a normal `Round` with `IsChampionship = true`; it is **excluded** from standings (it is the output, not an input).
- Standings sort: **Wins desc → Diff (PointsFor − PointsAgainst) desc → PointsFor desc → Player.Name asc**.
- Within-court pairing in the final: **Team1 = seed1 & seed4, Team2 = seed2 & seed3**.
- Court capacity for the final: `capacity = min(NumberOfCourts, floor(N/4))`; lowest seeds beyond capacity get byes. If `capacity == 0`, no championship round is created.
- The running app applies schema via `db.Database.Migrate()` (see `Program.cs`), so model changes **require a real EF migration**, not just `EnsureCreated`.
- Tests use in-memory SQLite with `EnsureCreated()` (reads the current model, so new properties are picked up without the migration).

---

### Task 1: Data model fields + EF migration

**Files:**
- Modify: `PickleballScheduler/Models/Match.cs`
- Modify: `PickleballScheduler/Models/Round.cs`
- Create: `PickleballScheduler/Migrations/<timestamp>_AddMatchScoresAndChampionship.cs` (generated)
- Test: `PickleballScheduler.Tests/Models/ScoreModelTests.cs`

**Interfaces:**
- Produces: `Match.Team1Score` (`int?`), `Match.Team2Score` (`int?`), `Round.IsChampionship` (`bool`, default `false`).

- [ ] **Step 1: Write the failing test**

Create `PickleballScheduler.Tests/Models/ScoreModelTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PickleballScheduler.Tests --filter ScoreModelTests`
Expected: FAIL — compile error, `Match` has no `Team1Score`/`Team2Score`, `Round` has no `IsChampionship`.

- [ ] **Step 3: Add the properties**

In `PickleballScheduler/Models/Match.cs`, add after `Team2Player2` (the last property, line 16):

```csharp
    public int? Team1Score { get; set; }
    public int? Team2Score { get; set; }
```

In `PickleballScheduler/Models/Round.cs`, add after `RoundNumber` (line 8):

```csharp
    public bool IsChampionship { get; set; }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PickleballScheduler.Tests --filter ScoreModelTests`
Expected: PASS (2 tests). The in-memory `EnsureCreated` picks up the new columns.

- [ ] **Step 5: Generate the EF migration for the running app**

Run: `dotnet ef migrations add AddMatchScoresAndChampionship --project PickleballScheduler`
Expected: creates `PickleballScheduler/Migrations/<timestamp>_AddMatchScoresAndChampionship.cs` adding `Team1Score`, `Team2Score` (nullable INTEGER) to `Matches` and `IsChampionship` (INTEGER/bool, default 0) to `Rounds`, and updates `AppDbContextModelSnapshot.cs`.

If `dotnet ef` is not installed, run `dotnet tool install --global dotnet-ef` first.

Verify the generated migration's `Up()` contains `AddColumn<int>(... "Team1Score" ...)`, `AddColumn<int>(... "Team2Score" ...)`, and `AddColumn<bool>(... "IsChampionship" ...)`.

- [ ] **Step 6: Build to confirm everything compiles**

Run: `dotnet build PickleballScheduler`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add PickleballScheduler/Models/Match.cs PickleballScheduler/Models/Round.cs PickleballScheduler/Migrations PickleballScheduler.Tests/Models/ScoreModelTests.cs
git commit -m "feat: add match score and championship-round model fields"
```

---

### Task 2: StandingsCalculator service

**Files:**
- Create: `PickleballScheduler/Services/StandingsCalculator.cs`
- Test: `PickleballScheduler.Tests/Services/StandingsCalculatorTests.cs`

**Interfaces:**
- Consumes: `Match.Team1Score`/`Team2Score`, `Round.IsChampionship` (Task 1); `Player`, `Round`, `Match` models.
- Produces:
  - `public record StandingsRow(Player Player, int Wins, int PointsFor, int PointsAgainst) { public int Diff => PointsFor - PointsAgainst; }`
  - `public static class StandingsCalculator { public static List<StandingsRow> Compute(IEnumerable<Player> players, IEnumerable<Round> rounds); }`
  - Returned list is sorted by Wins desc → Diff desc → PointsFor desc → Player.Name asc. Every player in `players` gets exactly one row (zeros if no scored matches). Rows where `Round.IsChampionship` is true and matches missing either score are ignored.

- [ ] **Step 1: Write the failing test**

Create `PickleballScheduler.Tests/Services/StandingsCalculatorTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PickleballScheduler.Tests --filter StandingsCalculatorTests`
Expected: FAIL — `StandingsCalculator` / `StandingsRow` do not exist.

- [ ] **Step 3: Implement the calculator**

Create `PickleballScheduler/Services/StandingsCalculator.cs`:

```csharp
using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public record StandingsRow(Player Player, int Wins, int PointsFor, int PointsAgainst)
{
    public int Diff => PointsFor - PointsAgainst;
}

public static class StandingsCalculator
{
    public static List<StandingsRow> Compute(IEnumerable<Player> players, IEnumerable<Round> rounds)
    {
        var wins = new Dictionary<int, int>();
        var pointsFor = new Dictionary<int, int>();
        var pointsAgainst = new Dictionary<int, int>();

        var playerList = players.ToList();
        foreach (var p in playerList)
        {
            wins[p.Id] = 0;
            pointsFor[p.Id] = 0;
            pointsAgainst[p.Id] = 0;
        }

        foreach (var round in rounds)
        {
            if (round.IsChampionship) continue;
            foreach (var m in round.Matches)
            {
                if (m.Team1Score is not int s1 || m.Team2Score is not int s2) continue;

                var team1 = new[] { m.Team1Player1Id, m.Team1Player2Id };
                var team2 = new[] { m.Team2Player1Id, m.Team2Player2Id };

                Accumulate(team1, s1, s2, s1 > s2);
                Accumulate(team2, s2, s1, s2 > s1);
            }
        }

        return playerList
            .Select(p => new StandingsRow(p, wins[p.Id], pointsFor[p.Id], pointsAgainst[p.Id]))
            .OrderByDescending(r => r.Wins)
            .ThenByDescending(r => r.Diff)
            .ThenByDescending(r => r.PointsFor)
            .ThenBy(r => r.Player.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        void Accumulate(int[] team, int scored, int conceded, bool won)
        {
            foreach (var id in team)
            {
                if (!wins.ContainsKey(id)) continue;
                if (won) wins[id]++;
                pointsFor[id] += scored;
                pointsAgainst[id] += conceded;
            }
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PickleballScheduler.Tests --filter StandingsCalculatorTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add PickleballScheduler/Services/StandingsCalculator.cs PickleballScheduler.Tests/Services/StandingsCalculatorTests.cs
git commit -m "feat: add StandingsCalculator for per-player rankings"
```

---

### Task 3: ChampionshipRoundBuilder

**Files:**
- Create: `PickleballScheduler/Services/ChampionshipRoundBuilder.cs`
- Test: `PickleballScheduler.Tests/Services/ChampionshipRoundBuilderTests.cs`

**Interfaces:**
- Consumes: `StandingsRow` (Task 2); `Round`, `Match`, `Bye` models; `Round.IsChampionship` (Task 1).
- Produces:
  - `public static class ChampionshipRoundBuilder { public static Round? Build(IReadOnlyList<StandingsRow> standings, int numberOfCourts, int nextRoundNumber); }`
  - Returns `null` when `capacity == min(numberOfCourts, standings.Count / 4) == 0`.
  - Otherwise returns a `Round` with `IsChampionship = true`, `RoundNumber = nextRoundNumber`, `capacity` matches (CourtNumber 1..capacity), Team1 = seed1 & seed4 / Team2 = seed2 & seed3 per court, and a `Bye` for every standings entry past `capacity * 4`.

- [ ] **Step 1: Write the failing test**

Create `PickleballScheduler.Tests/Services/ChampionshipRoundBuilderTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PickleballScheduler.Tests --filter ChampionshipRoundBuilderTests`
Expected: FAIL — `ChampionshipRoundBuilder` does not exist.

- [ ] **Step 3: Implement the builder**

Create `PickleballScheduler/Services/ChampionshipRoundBuilder.cs`:

```csharp
using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public static class ChampionshipRoundBuilder
{
    public static Round? Build(IReadOnlyList<StandingsRow> standings, int numberOfCourts, int nextRoundNumber)
    {
        int capacity = Math.Min(numberOfCourts, standings.Count / 4);
        if (capacity == 0) return null;

        var round = new Round { RoundNumber = nextRoundNumber, IsChampionship = true };

        for (int court = 0; court < capacity; court++)
        {
            var s1 = standings[court * 4 + 0].Player;
            var s2 = standings[court * 4 + 1].Player;
            var s3 = standings[court * 4 + 2].Player;
            var s4 = standings[court * 4 + 3].Player;

            round.Matches.Add(new Match
            {
                CourtNumber = court + 1,
                Team1Player1Id = s1.Id, Team1Player2Id = s4.Id,
                Team2Player1Id = s2.Id, Team2Player2Id = s3.Id,
            });
        }

        for (int i = capacity * 4; i < standings.Count; i++)
            round.Byes.Add(new Bye { PlayerId = standings[i].Player.Id });

        return round;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PickleballScheduler.Tests --filter ChampionshipRoundBuilderTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add PickleballScheduler/Services/ChampionshipRoundBuilder.cs PickleballScheduler.Tests/Services/ChampionshipRoundBuilderTests.cs
git commit -m "feat: add ChampionshipRoundBuilder for seeded final round"
```

---

### Task 4: EventService persistence methods

**Files:**
- Modify: `PickleballScheduler/Services/EventService.cs`
- Test: `PickleballScheduler.Tests/Services/EventServiceChampionshipTests.cs`

**Interfaces:**
- Consumes: `StandingsCalculator.Compute` (Task 2), `ChampionshipRoundBuilder.Build` (Task 3), existing `GetByIdAsync`.
- Produces (new public methods on `EventService`):
  - `Task SaveMatchScoreAsync(int matchId, int? team1Score, int? team2Score)`
  - `Task<Round?> GenerateChampionshipRoundAsync(int eventId)` — returns the created round, or `null` if there are too few players (capacity 0). Deletes any pre-existing championship round first.
  - `Task RemoveChampionshipRoundAsync(int eventId)`

- [ ] **Step 1: Write the failing test**

Create `PickleballScheduler.Tests/Services/EventServiceChampionshipTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PickleballScheduler.Tests --filter EventServiceChampionshipTests`
Expected: FAIL — the three new `EventService` methods do not exist.

- [ ] **Step 3: Implement the methods**

In `PickleballScheduler/Services/EventService.cs`, add these methods inside the class (e.g. after `SwapPlayersAsync`, before `DeleteAsync`):

```csharp
    public async Task SaveMatchScoreAsync(int matchId, int? team1Score, int? team2Score)
    {
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);
        if (match == null) return;
        match.Team1Score = team1Score;
        match.Team2Score = team2Score;
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Computes standings from the event's regular (non-championship) rounds and builds a
    /// final round seeding the strongest players onto the lowest-numbered courts. Any existing
    /// championship round is replaced. Returns the created round, or null if there are too few
    /// players to fill a single court of four.
    /// </summary>
    public async Task<Round?> GenerateChampionshipRoundAsync(int eventId)
    {
        var evt = await GetByIdAsync(eventId);
        if (evt == null) return null;

        await RemoveChampionshipRoundAsync(eventId);

        var players = evt.EventPlayers.Select(ep => ep.Player).ToList();
        var regularRounds = evt.Rounds.Where(r => !r.IsChampionship).ToList();
        var standings = StandingsCalculator.Compute(players, regularRounds);

        int nextRoundNumber = (evt.Rounds.Where(r => !r.IsChampionship)
            .Select(r => (int?)r.RoundNumber).Max() ?? 0) + 1;

        var round = ChampionshipRoundBuilder.Build(standings, evt.NumberOfCourts, nextRoundNumber);
        if (round == null) return null;

        round.EventId = eventId;
        _db.Rounds.Add(round);
        await _db.SaveChangesAsync();
        return round;
    }

    public async Task RemoveChampionshipRoundAsync(int eventId)
    {
        var champ = await _db.Rounds
            .Where(r => r.EventId == eventId && r.IsChampionship)
            .Include(r => r.Matches)
            .Include(r => r.Byes)
            .ToListAsync();

        foreach (var r in champ)
        {
            _db.Matches.RemoveRange(r.Matches);
            _db.Byes.RemoveRange(r.Byes);
        }
        _db.Rounds.RemoveRange(champ);
        await _db.SaveChangesAsync();
    }
```

Note: `RemoveChampionshipRoundAsync` is called inside `GenerateChampionshipRoundAsync` on the same `_db`; it issues its own `SaveChangesAsync`, which is fine.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PickleballScheduler.Tests --filter EventServiceChampionshipTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Run the whole suite to confirm no regressions**

Run: `dotnet test PickleballScheduler.Tests`
Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add PickleballScheduler/Services/EventService.cs PickleballScheduler.Tests/Services/EventServiceChampionshipTests.cs
git commit -m "feat: add score-saving and championship-round persistence to EventService"
```

---

### Task 5: Schedule.razor UI — score inputs, standings, generate/remove

**Files:**
- Modify: `PickleballScheduler/Components/Pages/Schedule.razor`

**Interfaces:**
- Consumes: `EventService.SaveMatchScoreAsync`, `GenerateChampionshipRoundAsync`, `RemoveChampionshipRoundAsync` (Task 4); `StandingsCalculator.Compute` + `StandingsRow` (Task 2); `Round.IsChampionship`, `Match.Team1Score`/`Team2Score` (Task 1).
- Produces: UI only (no new public interface).

This task is UI and is verified by build + manual run (the project has no Blazor component tests). Make each edit, then build, then exercise the page.

- [ ] **Step 1: Add score inputs to each match cell**

In `Schedule.razor`, replace the match-cell rendering block (currently lines 99–106, the `<td class="match-cell">` containing the two team `<div>`s and the `vs`) with a version that adds a score input per team. The match object is mutated in place and saved on change:

```razor
                                <td class="match-cell">
                                    @if (match != null)
                                    {
                                        <div class="team-line">
                                            <span>@PlayerSpan(round.Id, match.Team1Player1) &amp; @PlayerSpan(round.Id, match.Team1Player2)</span>
                                            <input type="number" min="0" class="score-input"
                                                   value="@match.Team1Score"
                                                   @onchange="@(e => OnScoreChanged(match, team1: true, e.Value?.ToString()))" />
                                        </div>
                                        <div class="vs">vs</div>
                                        <div class="team-line">
                                            <span>@PlayerSpan(round.Id, match.Team2Player1) &amp; @PlayerSpan(round.Id, match.Team2Player2)</span>
                                            <input type="number" min="0" class="score-input"
                                                   value="@match.Team2Score"
                                                   @onchange="@(e => OnScoreChanged(match, team1: false, e.Value?.ToString()))" />
                                        </div>
                                    }
                                </td>
```

- [ ] **Step 2: Add the score-change handler**

In the `@code` block, add:

```csharp
    private async Task OnScoreChanged(Match match, bool team1, string? raw)
    {
        int? value = int.TryParse(raw, out var v) && v >= 0 ? v : null;
        if (team1) match.Team1Score = value;
        else match.Team2Score = value;

        await EventService.SaveMatchScoreAsync(match.Id, match.Team1Score, match.Team2Score);
        // standings recompute on the re-render that follows this handler
    }
```

- [ ] **Step 3: Label the championship round and style its row**

In the rounds loop, change the round-number cell (currently line 94, `<td class="round-num">@round.RoundNumber</td>`) and its row to distinguish the championship round:

```razor
                        <tr class="@(round.IsChampionship ? "champ-row" : null)">
                            <td class="round-num">@(round.IsChampionship ? "🏆" : round.RoundNumber.ToString())</td>
```

Ensure the rounds are ordered so the championship (highest round number) sorts last — the existing `evt.Rounds.OrderBy(r => r.RoundNumber)` already does this since `GenerateChampionshipRoundAsync` assigns it `max + 1`.

- [ ] **Step 4: Add Generate / Remove buttons and the unscored-warning modal**

In the `no-print` toolbar `<div>` (currently lines 15–25), add after the Regenerate button:

```razor
        <button class="btn btn-outline-primary btn-sm me-2" @onclick="OnGenerateChampionshipClicked" disabled="@isGenerating">
            @(isGenerating ? "Generating..." : (HasChampionship ? "Regenerate Championship Round" : "Generate Championship Round"))
        </button>
        @if (HasChampionship)
        {
            <button class="btn btn-outline-danger btn-sm" @onclick="RemoveChampionship">Remove Championship Round</button>
        }
```

Add the warning modal near the regenerate modal (after line 63):

```razor
    @if (showUnscoredWarning)
    {
        <div class="modal d-block no-print" tabindex="-1" role="dialog" style="background-color: rgba(0,0,0,0.5);">
            <div class="modal-dialog" role="document">
                <div class="modal-content">
                    <div class="modal-header">
                        <h5 class="modal-title">Some matches have no score</h5>
                        <button type="button" class="btn-close" aria-label="Close" @onclick="@(() => showUnscoredWarning = false)"></button>
                    </div>
                    <div class="modal-body">
                        <p class="mb-0">⚠️ @unscoredCount of @totalMatchCount matches are unscored. Standings will use only the scores you've entered.</p>
                    </div>
                    <div class="modal-footer">
                        <button type="button" class="btn btn-secondary" @onclick="@(() => showUnscoredWarning = false)">Cancel</button>
                        <button type="button" class="btn btn-primary" @onclick="ConfirmGenerateChampionship">Generate anyway</button>
                    </div>
                </div>
            </div>
        </div>
    }

    @if (champNotEnoughPlayers)
    {
        <div class="alert alert-warning py-2 no-print" role="alert">
            Not enough players for a championship court (need at least 4). Enter more players or scores first.
        </div>
    }
```

- [ ] **Step 5: Add the generate/remove handlers and helpers**

In `@code`, add fields and methods:

```csharp
    private bool isGenerating;
    private bool showUnscoredWarning;
    private bool champNotEnoughPlayers;
    private int unscoredCount;
    private int totalMatchCount;

    private bool HasChampionship => evt?.Rounds.Any(r => r.IsChampionship) ?? false;

    private async Task OnGenerateChampionshipClicked()
    {
        if (evt == null) return;
        var regularMatches = evt.Rounds.Where(r => !r.IsChampionship).SelectMany(r => r.Matches).ToList();
        totalMatchCount = regularMatches.Count;
        unscoredCount = regularMatches.Count(m => m.Team1Score is null || m.Team2Score is null);

        if (unscoredCount > 0)
        {
            showUnscoredWarning = true;
            return;
        }
        await GenerateChampionship();
    }

    private async Task ConfirmGenerateChampionship()
    {
        showUnscoredWarning = false;
        await GenerateChampionship();
    }

    private async Task GenerateChampionship()
    {
        if (evt == null || isGenerating) return;
        isGenerating = true;
        champNotEnoughPlayers = false;
        try
        {
            var created = await EventService.GenerateChampionshipRoundAsync(evt.Id);
            champNotEnoughPlayers = created == null;
            evt = await EventService.GetByIdAsync(EventId);
            courtNames = evt?.GetCourtNamesList() ?? new();
        }
        finally
        {
            isGenerating = false;
        }
    }

    private async Task RemoveChampionship()
    {
        if (evt == null) return;
        await EventService.RemoveChampionshipRoundAsync(evt.Id);
        evt = await EventService.GetByIdAsync(EventId);
        courtNames = evt?.GetCourtNamesList() ?? new();
    }
```

- [ ] **Step 6: Render the standings table below the schedule**

Inside the `schedule-sheet` div, after the `</table>` that closes `sheet-table` (currently line 122), add:

```razor
            @{ var standings = StandingsCalculator.Compute(
                   evt.EventPlayers.Select(ep => ep.Player).ToList(), evt.Rounds); }
            @if (standings.Any(r => r.PointsFor > 0 || r.PointsAgainst > 0))
            {
                <h2 class="standings-title">Standings</h2>
                <table class="sheet-table standings-table">
                    <thead>
                        <tr><th>#</th><th class="text-start">Player</th><th>W</th><th>Diff</th><th>Pts</th></tr>
                    </thead>
                    <tbody>
                        @{ int rank = 1; }
                        @foreach (var row in standings)
                        {
                            <tr>
                                <td>@rank</td>
                                <td class="text-start">@row.Player.Name</td>
                                <td>@row.Wins</td>
                                <td>@(row.Diff > 0 ? $"+{row.Diff}" : row.Diff.ToString())</td>
                                <td>@row.PointsFor</td>
                            </tr>
                            rank++;
                        }
                    </tbody>
                </table>
            }
```

- [ ] **Step 7: Add CSS for score inputs, champ row, and standings**

At the bottom of `Schedule.razor`, add a `<style>` block (or extend an existing one if present):

```razor
<style>
    .score-input { width: 3rem; margin-left: 0.4rem; text-align: center; }
    .team-line { display: flex; align-items: center; justify-content: space-between; }
    .champ-row { background-color: #fff3cd; }
    .standings-title { font-size: 1.1rem; margin-top: 1rem; }
    .standings-table { width: auto; }
    @@media print {
        .score-input { border: none; width: auto; -webkit-appearance: none; appearance: none; }
        .score-input::-webkit-outer-spin-button, .score-input::-webkit-inner-spin-button { display: none; }
    }
</style>
```

(Note the `@@media` — single `@` is the Razor transition; `@@` emits a literal `@`.)

- [ ] **Step 8: Update the Regenerate confirm text**

In the regenerate modal body (currently line 53), replace the paragraph with:

```razor
                        <p class="mb-0">This will replace the current schedule with a new one. The existing rounds, matches, <strong>entered scores, and any championship round</strong> will be deleted.</p>
```

- [ ] **Step 9: Build**

Run: `dotnet build PickleballScheduler`
Expected: Build succeeded.

- [ ] **Step 10: Manual verification**

Run: `dotnet run --project PickleballScheduler`, open an event's schedule with a generated round, then verify:
1. Each match cell shows two score boxes; entering numbers and clicking away persists them (reload the page — values remain).
2. The **Standings** table appears once any match is scored and ranks players (Wins, then Diff, then Pts).
3. "Generate Championship Round" with unscored matches shows the warning modal; "Generate anyway" creates a 🏆 row as the last round, top seeds on court 1.
4. The button relabels to "Regenerate Championship Round" and a "Remove Championship Round" button appears; Remove deletes the 🏆 row.
5. With fewer than 4 players, generating shows the "Not enough players" notice and creates no round.
6. Print preview (Print Schedule) shows scores as plain numbers and includes the Standings table.

- [ ] **Step 11: Commit**

```bash
git add PickleballScheduler/Components/Pages/Schedule.razor
git commit -m "feat: score entry, standings table, and championship round UI"
```

---

## Self-Review notes

- **Spec coverage:** model fields (T1), standings metric + sort + exclusions (T2), balanced pairing + lowest-seed byes + capacity + N<4 (T3), save/generate/replace/remove persistence + regenerate-clears-scores via existing `SaveScheduleAsync` (T4), inline inputs + warn-on-unscored + standings view/print + champ label + regenerate text (T5). All spec sections map to a task.
- **Type consistency:** `StandingsRow(Player, int Wins, int PointsFor, int PointsAgainst)` with computed `Diff`, `StandingsCalculator.Compute(IEnumerable<Player>, IEnumerable<Round>)`, `ChampionshipRoundBuilder.Build(IReadOnlyList<StandingsRow>, int, int) -> Round?`, and the three `EventService` async methods are referenced identically across tasks.
- **Migration vs tests:** tests rely on `EnsureCreated` (model-driven); the app relies on `Migrate()`, so Task 1 generates a real migration.
