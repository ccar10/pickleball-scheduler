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

    public async Task<List<Event>> GetByUserAsync(int userId)
        => await _db.Events
            .Include(e => e.EventPlayers)
            .Where(e => e.UserId == userId)
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

    public async Task<Event> CreateAsync(string name, DateTime date, int courts, string courtNames, int? userId)
    {
        var evt = new Event
        {
            Name = name,
            Date = date,
            NumberOfCourts = courts,
            CourtNames = courtNames,
            UserId = userId
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

    public async Task SaveScheduleAsync(int eventId, ScheduleResult result)
    {
        var evt = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId);
        if (evt == null) return;

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

        foreach (var round in result.Rounds)
        {
            round.EventId = eventId;
            _db.Rounds.Add(round);
        }

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Swaps the positions of two players within a single round and saves.
    /// Court slots are updated in place; byes are deleted and re-inserted because
    /// a bye's player id is part of its primary key and cannot be mutated.
    /// </summary>
    public async Task SwapPlayersAsync(int roundId, int playerIdA, int playerIdB)
    {
        if (playerIdA == playerIdB) return;

        var round = await _db.Rounds
            .Where(r => r.Id == roundId)
            .Include(r => r.Matches)
            .Include(r => r.Byes)
            .FirstOrDefaultAsync();
        if (round == null) return;

        RoundEditor.SwapInMatches(round, playerIdA, playerIdB);

        // Byes: delete any whose player is being swapped, then re-add with the
        // swapped player id (primary key change requires delete + insert).
        var changedByes = round.Byes
            .Where(b => b.PlayerId == playerIdA || b.PlayerId == playerIdB)
            .ToList();
        foreach (var bye in changedByes)
        {
            _db.Byes.Remove(bye);
            var newPlayerId = bye.PlayerId == playerIdA ? playerIdB : playerIdA;
            _db.Byes.Add(new Bye { RoundId = roundId, PlayerId = newPlayerId });
        }

        await _db.SaveChangesAsync();
    }

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
