namespace PokerCoach.Domain.Poker.Analysis;

/// <summary>
/// What the hero did in one hand, as the flags statistics count. Each "made" flag has its opportunity
/// flag: a rate is made / opportunities, never made / hands. Computed by <see cref="HandAnalyzer"/>;
/// <see cref="Version"/> changes whenever a definition changes, so stored facts are recomputed.
/// </summary>
/// <param name="Position">Null when it cannot be named (more than 9 players dealt).</param>
/// <param name="HadPreflopDecision">The hero acted voluntarily preflop (not a walk, not all-in from the blinds).</param>
/// <param name="NetChips">Chips won minus chips put in, antes and blinds included.</param>
/// <param name="AllInEquity">Preflop all-in with every hand shown: the hero's share of the main pot. Null otherwise.</param>
/// <param name="AllInExpectedNetChips">Same hands: what the hero should have won given the cards (see <see cref="AllInExpectation"/>).</param>
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
    decimal NetBigBlinds,
    decimal? AllInEquity = null,
    decimal? AllInExpectedNetChips = null)
{
    /// <summary>Bump on any change of a definition below or in <see cref="HandAnalyzer"/>.</summary>
    /// <remarks>2: all-in EV.</remarks>
    public const int Version = 2;

    /// <summary>Expected result of an all-in in big blinds, comparable across levels and tournaments.</summary>
    public decimal? AllInExpectedNetBigBlinds(long bigBlind) =>
        AllInExpectedNetChips is { } chips && bigBlind > 0 ? Math.Round(chips / bigBlind, 2) : null;
}
