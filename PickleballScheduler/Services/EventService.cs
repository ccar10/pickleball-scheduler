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
