namespace PickleballScheduler.Models;

public class Bye
{
    public int RoundId { get; set; }
    public Round Round { get; set; } = null!;
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
}
