namespace PickleballScheduler.Models;

public class Event
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public int NumberOfCourts { get; set; } = 1;
    public string CourtNames { get; set; } = "";
    public List<EventPlayer> EventPlayers { get; set; } = new();
    public List<Round> Rounds { get; set; } = new();

    /// <summary>
    /// Returns the list of court names parsed from the semicolon-delimited CourtNames field.
    /// Falls back to "Court 1", "Court 2", etc. if empty.
    /// </summary>
    public List<string> GetCourtNamesList()
    {
        if (!string.IsNullOrWhiteSpace(CourtNames))
        {
            var names = CourtNames.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(n => n.Trim())
                .Where(n => n.Length > 0)
                .ToList();
            if (names.Count > 0) return names;
        }
        return Enumerable.Range(1, NumberOfCourts).Select(i => $"Court {i}").ToList();
    }
}
