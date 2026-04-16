namespace PickleballScheduler.Models;

public class Match
{
    public int Id { get; set; }
    public int RoundId { get; set; }
    public Round Round { get; set; } = null!;
    public int CourtNumber { get; set; }
    public int Team1Player1Id { get; set; }
    public Player Team1Player1 { get; set; } = null!;
    public int Team1Player2Id { get; set; }
    public Player Team1Player2 { get; set; } = null!;
    public int Team2Player1Id { get; set; }
    public Player Team2Player1 { get; set; } = null!;
    public int Team2Player2Id { get; set; }
    public Player Team2Player2 { get; set; } = null!;
}
