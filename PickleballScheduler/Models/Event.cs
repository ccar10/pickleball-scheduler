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
