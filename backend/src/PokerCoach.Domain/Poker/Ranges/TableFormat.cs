namespace PokerCoach.Domain.Poker.Ranges;

/// <summary>
/// The table a hand was played at, by its number of seats (not by how many were dealt in: a 9-max table
/// down to six players still plays full-ring positions). Values travel in the API.
/// </summary>
public enum TableFormat
{
    /// <summary>Up to six seats: UTG, HJ, CO, BTN, SB, BB.</summary>
    SixMax,

    /// <summary>Seven seats or more: UTG, UTG+1, UTG+2, LJ, HJ, CO, BTN, SB, BB.</summary>
    FullRing,
}

public static class TableFormats
{
    public static TableFormat Of(int maxSeats) => maxSeats <= 6 ? TableFormat.SixMax : TableFormat.FullRing;
}
