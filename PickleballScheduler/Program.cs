using Microsoft.EntityFrameworkCore;
using PickleballScheduler.Components;
using PickleballScheduler.Data;
using PickleballScheduler.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var dbPath = builder.Configuration.GetConnectionString("DefaultConnection")!;
var dataDir = Path.GetDirectoryName(dbPath.Replace("Data Source=", ""));
if (!string.IsNullOrEmpty(dataDir))
{
    Directory.CreateDirectory(dataDir);
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(dbPath));

builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<ScheduleGenerator>();

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
    try
    {
        db.Database.Migrate();
    }
    catch
    {
        // Schema mismatch — recreate from scratch (data is ephemeral)
        db.Database.EnsureDeleted();
        db.Database.Migrate();
    }

    var guest = db.Users.FirstOrDefault(u => u.Name == UserService.GuestName);
    if (guest == null)
    {
        guest = new PickleballScheduler.Models.User { Name = UserService.GuestName };
        db.Users.Add(guest);
        db.SaveChanges();
    }

    var orphans = db.Events.Where(e => e.UserId == null).ToList();
    if (orphans.Count > 0)
    {
        foreach (var e in orphans) e.UserId = guest.Id;
        db.SaveChanges();
    }
}

app.Run();
