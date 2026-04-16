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

    public async Task<Player> CreateAsync(string name)
    {
        var player = new Player { Name = name };
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
