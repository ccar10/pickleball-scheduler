using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

/// <summary>
/// Manual, within-round edits to a generated schedule. Each player appears
/// exactly once in a round, so a player id uniquely identifies their slot.
/// </summary>
public static class RoundEditor
{
    /// <summary>
    /// Swaps the positions of two players within a single round. Both players
    /// must currently be in the round (on a court or on a bye).
    /// </summary>
    public static void SwapPlayers(Round round, int playerIdA, int playerIdB)
    {
        if (playerIdA == playerIdB) return;

        SwapInMatches(round, playerIdA, playerIdB);

        foreach (var bye in round.Byes)
        {
            if (bye.PlayerId == playerIdA) bye.PlayerId = playerIdB;
            else if (bye.PlayerId == playerIdB) bye.PlayerId = playerIdA;
        }
    }

    /// <summary>
    /// Swaps two players only within this round's match (court) slots, leaving
    /// byes untouched. The persistence layer handles byes separately because a
    /// bye's player id is part of its primary key and cannot be mutated in place.
    /// </summary>
    public static void SwapInMatches(Round round, int playerIdA, int playerIdB)
    {
        if (playerIdA == playerIdB) return;

        foreach (var match in round.Matches)
        {
            if (match.Team1Player1Id == playerIdA) match.Team1Player1Id = playerIdB;
            else if (match.Team1Player1Id == playerIdB) match.Team1Player1Id = playerIdA;

            if (match.Team1Player2Id == playerIdA) match.Team1Player2Id = playerIdB;
            else if (match.Team1Player2Id == playerIdB) match.Team1Player2Id = playerIdA;

            if (match.Team2Player1Id == playerIdA) match.Team2Player1Id = playerIdB;
            else if (match.Team2Player1Id == playerIdB) match.Team2Player1Id = playerIdA;

            if (match.Team2Player2Id == playerIdA) match.Team2Player2Id = playerIdB;
            else if (match.Team2Player2Id == playerIdB) match.Team2Player2Id = playerIdA;
        }
    }
}
