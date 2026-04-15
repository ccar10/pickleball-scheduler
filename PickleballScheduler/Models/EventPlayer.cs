namespace PickleballScheduler.Models;

public class EventPlayer
{
    public int EventId { get; set; }
    public Event Event { get; set; } = null!;
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
}
