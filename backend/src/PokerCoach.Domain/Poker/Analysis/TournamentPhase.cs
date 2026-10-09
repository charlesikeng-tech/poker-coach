namespace PokerCoach.Domain.Poker.Analysis;

/// <summary>
/// Where a hand sits in its tournament, by blind level. Hand histories do not say how many players are
/// left, so the bubble, the money and the final table cannot be told without guessing; the level is
/// printed on every hand, and on Winamax the blinds of a level number are the same whatever the speed
/// (only its duration changes), which makes it comparable from one tournament to the next.
/// </summary>
public enum TournamentPhase
{
    /// <summary>Levels 1–6: deep stacks, small antes.</summary>
    Early,

    /// <summary>Levels 7–12: antes matter, stacks shorten.</summary>
    Middle,

    /// <summary>Level 13 and later: short stacks, push/fold, the money and beyond.</summary>
    Late,
}

public static class TournamentPhases
{
    public const int LastEarlyLevel = 6;

    public const int LastMiddleLevel = 12;

    public static TournamentPhase Of(int level) => level switch
    {
        <= LastEarlyLevel => TournamentPhase.Early,
        <= LastMiddleLevel => TournamentPhase.Middle,
        _ => TournamentPhase.Late,
    };

    /// <summary>Inclusive level bounds; null max for the last phase.</summary>
    public static (int Min, int? Max) Levels(TournamentPhase phase) => phase switch
    {
        TournamentPhase.Early => (1, LastEarlyLevel),
        TournamentPhase.Middle => (LastEarlyLevel + 1, LastMiddleLevel),
        _ => (LastMiddleLevel + 1, null),
    };
}
