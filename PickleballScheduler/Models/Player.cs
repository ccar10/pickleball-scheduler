namespace PickleballScheduler.Models;

public class Player
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public decimal? DuprRating { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
