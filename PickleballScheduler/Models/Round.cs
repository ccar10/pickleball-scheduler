namespace PickleballScheduler.Models;

public class Round
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event Event { get; set; } = null!;
    public int RoundNumber { get; set; }
    public List<Match> Matches { get; set; } = new();
    public List<Bye> Byes { get; set; } = new();
}
