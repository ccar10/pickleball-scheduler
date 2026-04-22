using Microsoft.EntityFrameworkCore;
using PickleballScheduler.Data;
using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public class UserService
{
    public const string GuestName = "Guest";

    private readonly AppDbContext _db;

    public UserService(AppDbContext db) => _db = db;

    public async Task<List<User>> GetAllAsync()
        => await _db.Users.OrderBy(u => u.Name).ToListAsync();

    public async Task<User?> GetByIdAsync(int id)
        => await _db.Users.FirstOrDefaultAsync(u => u.Id == id);

    public async Task<User> GetOrCreateAsync(string name)
    {
        var trimmed = name.Trim();
        var existing = await _db.Users.FirstOrDefaultAsync(u => u.Name.ToLower() == trimmed.ToLower());
        if (existing != null) return existing;

        var user = new User { Name = trimmed };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task<(bool ok, string? error)> RenameAsync(int userId, string newName)
    {
        var trimmed = newName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return (false, "Name can't be empty.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return (false, "User not found.");
        if (user.Name == GuestName) return (false, "Guest can't be renamed.");

        var collision = await _db.Users
            .FirstOrDefaultAsync(u => u.Id != userId && u.Name.ToLower() == trimmed.ToLower());
        if (collision != null) return (false, "A user with that name already exists.");

        user.Name = trimmed;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task DeleteAsync(int userId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null || user.Name == GuestName) return;

        var guest = await _db.Users.FirstOrDefaultAsync(u => u.Name == GuestName);
        if (guest != null)
        {
            var userEvents = await _db.Events.Where(e => e.UserId == userId).ToListAsync();
            foreach (var e in userEvents) e.UserId = guest.Id;
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
    }
}
