# Pickleball Round-Robin Scheduler — Design Spec

## Overview

A Blazor Server web application that generates round-robin pickleball schedules for an arbitrary number of players. The organizer enters participant names and optional DUPR ratings, and the app produces a schedule where each player partners with a different person each round. Scores are recorded per match and standings are calculated automatically.

Hosted on Azure App Service (Free tier) with SQLite for near-zero cost. Designed for 3-5 simultaneous users.

## Data Model

### Player
| Field | Type | Notes |
|-------|------|-------|
| Id | int (PK) | Auto-increment |
| Name | string | Required |
| DuprRating | decimal? | Optional DUPR rating |
| CreatedDate | DateTime | When the player was added |

Players are persisted across events so names can be reused.

### Event
| Field | Type | Notes |
|-------|------|-------|
| Id | int (PK) | Auto-increment |
| Name | string | e.g., "Tuesday Night Pickles" |
| Date | DateTime | Event date |
| UseSkillBalancing | bool | Whether to balance teams by DUPR rating |
| NumberOfCourts | int | Available courts |

### EventPlayer
| Field | Type | Notes |
|-------|------|-------|
| EventId | int (FK → Event) | Composite PK |
| PlayerId | int (FK → Player) | Composite PK |

Join table linking players to events.

### Round
| Field | Type | Notes |
|-------|------|-------|
| Id | int (PK) | Auto-increment |
| EventId | int (FK → Event) | |
| RoundNumber | int | 1-based round index |

### Match
| Field | Type | Notes |
|-------|------|-------|
| Id | int (PK) | Auto-increment |
| RoundId | int (FK → Round) | |
| CourtNumber | int | Which court this match is on |
| Team1Player1Id | int (FK → Player) | |
| Team1Player2Id | int (FK → Player) | |
| Team2Player1Id | int (FK → Player) | |
| Team2Player2Id | int (FK → Player) | |
| Team1Score | int? | Null until scored |
| Team2Score | int? | Null until scored |
| IsComplete | bool | Whether score has been entered |

### Bye
| Field | Type | Notes |
|-------|------|-------|
| RoundId | int (FK → Round) | Composite PK |
| PlayerId | int (FK → Player) | Composite PK |

Tracks players sitting out a round when player count isn't a multiple of 4.

## Pages & User Flow

### 1. Home / Events (`/`)
- Lists all events, sorted by date (newest first)
- Each event shows name, date, and player count
- "New Event" button to create a new event
- Clicking an event navigates to its schedule

### 2. Event Setup (`/event/{id}/setup` or `/event/new`)
- Event name, date, and number of courts inputs
- Skill balancing toggle (checkbox)
- Player roster: checkboxes to select from existing saved players
- "Add new player" inline form (name + optional DUPR rating) — adds to the persistent roster and auto-selects them
- "Generate Schedule" button — runs the algorithm and navigates to the schedule view
- Validation: minimum 4 players required, at least 1 court

### 3. Schedule View (`/event/{id}/schedule`)
- Displays all rounds, each showing:
  - Round number header
  - Match cards per court: "Player A & Player B **vs** Player C & Player D"
  - Bye list: players sitting out this round (if any)
- Inline score entry on each match card: two number inputs for Team 1 and Team 2 scores
- Scores save on change (no separate save button)
- Visual indicator for completed matches (checkmark or highlight)

### 4. Standings (`/event/{id}/standings`)
- Table with columns: Rank, Player Name, Wins, Losses, Point Differential (+/-)
- Sorted by wins descending, then point differential descending
- A win/loss is counted per player: if your team wins, you get a win
- Point differential: sum of (your team's score - opponent's score) across all matches

### Navigation
- Top nav bar with app name ("Pickleball Scheduler") and links to Home
- Event pages have a secondary nav: Setup | Schedule | Standings
- Linear flow encouraged but all pages accessible at any time

## Scheduling Algorithm

### Core Logic
1. Collect the list of participating players for the event
2. If player count is not a multiple of 4, determine who sits out each round (bye assignment)
3. Generate rounds where:
   - No player has the same partner twice across rounds
   - Matchups (team vs team) vary as much as possible
   - Byes are distributed evenly (track sit-out count, assign bye to whoever has sat out least)

### Approach: Greedy with Constraint Tracking
1. Maintain a set of "used partnerships" (pairs of player IDs that have already been partners)
2. For each round:
   a. Determine bye players (if needed) — pick players with the fewest sit-outs
   b. From remaining active players, generate candidate pairings that haven't been used
   c. Greedily select pairings to form teams, then pair teams into matches
   d. Assign matches to courts
   e. Record the partnerships as used
3. Continue until the requested number of rounds is generated

### Skill Balancing Mode
When `UseSkillBalancing` is enabled:
- Sort players by DUPR rating (players without ratings placed in the middle)
- Pair from opposite ends: highest with lowest, second highest with second lowest, etc.
- Apply this pairing strategy each round, rotating which specific high/low combinations occur
- This replaces the variety-first greedy approach with a balance-first approach

### Number of Rounds
- Default: `players - 1` rounds — the theoretical max before partner repeats become unavoidable (e.g., 8 players = 7 rounds)
- Organizer can override to set fewer rounds
- The number of courts limits how many matches happen per round: `floor(active_players / 4)` matches per round, capped by court count

## Technology Stack

| Component | Choice | Rationale |
|-----------|--------|-----------|
| Framework | .NET 8 Blazor Server | Single deployment, SignalR for live updates, direct DB access |
| Database | SQLite via EF Core | Zero cost, zero infrastructure, sufficient for 3-5 users |
| CSS | Bootstrap 5 | Included with Blazor template, responsive out of the box |
| Hosting | Azure App Service Free (F1) | $0/month, adequate for low traffic |
| ORM | Entity Framework Core | Migrations, LINQ queries, standard .NET tooling |

## Project Structure

```
PickleballScheduler/
├── Data/
│   ├── AppDbContext.cs
│   └── Migrations/
├── Models/
│   ├── Player.cs
│   ├── Event.cs
│   ├── EventPlayer.cs
│   ├── Round.cs
│   ├── Match.cs
│   └── Bye.cs
├── Services/
│   ├── PlayerService.cs
│   ├── EventService.cs
│   ├── ScheduleGenerator.cs
│   └── StandingsService.cs
├── Components/
│   ├── Layout/
│   │   └── MainLayout.razor
│   └── Pages/
│       ├── Home.razor
│       ├── EventSetup.razor
│       ├── Schedule.razor
│       └── Standings.razor
├── wwwroot/
├── Program.cs
├── appsettings.json
└── PickleballScheduler.csproj
```

### Service Responsibilities
- **PlayerService**: CRUD operations for the player roster
- **EventService**: Create/read/update events, manage event-player associations, save match scores
- **ScheduleGenerator**: Implements the round-robin algorithm, returns a list of rounds with matches and byes
- **StandingsService**: Queries match results and computes win/loss/point-differential standings

## Error Handling & Edge Cases

- **Fewer than 4 players**: Show validation error on setup page — need at least 4 for a doubles match
- **Exactly 4 players**: Single match per round, partners rotate. No byes needed.
- **5-7 players**: 1 court active, 1-3 players on bye per round. Bye rotation keeps it fair.
- **Odd multiples of 4 + extras**: Mix of full courts and bye rotation
- **More players than courts can hold**: Some players sit out each round even beyond the "not divisible by 4" case. Bye distribution still applies.
- **No DUPR ratings with skill balancing on**: Players without ratings are treated as middle-of-the-pack for pairing purposes.
- **Re-generating schedule**: If the organizer re-generates after scores have been entered, warn that scores will be lost.

## Deployment

- Azure App Service Free (F1) tier
- SQLite database file at `Data/pickleball.db` (configurable via `appsettings.json`)
- Consider periodic backup of the SQLite file to Azure Blob Storage if data becomes important
- Publish via `dotnet publish` + Azure CLI or GitHub Actions
