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
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Event>()
            .HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.SetNull);

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
