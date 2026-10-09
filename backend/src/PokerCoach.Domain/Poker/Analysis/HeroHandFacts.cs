namespace PokerCoach.Domain.Poker.Analysis;

/// <summary>
/// What the hero did in one hand, as the flags statistics count. Each "made" flag has its opportunity
/// flag: a rate is made / opportunities, never made / hands. Computed by <see cref="HandAnalyzer"/>;
/// <see cref="Version"/> changes whenever a definition changes, so stored facts are recomputed.
/// </summary>
/// <param name="Position">Null when it cannot be named (more than 9 players dealt).</param>
/// <param name="HadPreflopDecision">The hero acted voluntarily preflop (not a walk, not all-in from the blinds).</param>
/// <param name="NetChips">Chips won minus chips put in, antes and blinds included.</param>
public sealed record HeroHandFacts(
    PokerPosition? Position,
    int PlayersDealt,
    decimal StackInBigBlinds,
    bool HadPreflopDecision,
    bool Vpip,
    bool Pfr,
    bool RfiOpportunity,
    bool Rfi,
    bool Limp,
    bool StealOpportunity,
    bool Steal,
    bool ThreeBetOpportunity,
    bool ThreeBet,
    bool FoldToThreeBetOpportunity,
    bool FoldToThreeBet,
    bool SawFlop,
    bool CbetFlopOpportunity,
    bool CbetFlop,
    bool WentToShowdown,
    bool WonAtShowdown,
    long NetChips,
    decimal NetBigBlinds)
{
    /// <summary>Bump on any change of a definition below or in <see cref="HandAnalyzer"/>.</summary>
    public const int Version = 1;
}
