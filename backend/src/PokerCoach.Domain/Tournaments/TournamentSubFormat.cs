namespace PokerCoach.Domain.Tournaments;

/// <summary>Tournament format family, room-agnostic. Null on a <see cref="PokerCoach.Domain.Progress.WeeklyPlan"/> means no sub-format filter (combined).</summary>
public enum TournamentSubFormat
{
    /// <summary>Multi-table tournament, standard field (50+ players).</summary>
    Mtt = 1,

    /// <summary>Rebuy / add-on MTT: chip EV applies during the rebuy period.</summary>
    RebuyMtt = 2,

    /// <summary>Sit-and-go: fixed small field (6–9 players), fast ICM from the first level.</summary>
    Sng = 3,
}
