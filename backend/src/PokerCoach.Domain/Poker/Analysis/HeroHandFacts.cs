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
/// <param name="PostflopAggressive">Bets and raises after the flop; <paramref name="PostflopDecisions"/> adds calls and folds.</param>
/// <param name="FacingOpenFrom">
/// The hero's first preflop decision faced exactly one raise, not all-in, with nobody limping or calling before
/// him: the opener's position. Null for any other spot (or an opener that cannot be named).
/// </param>
/// <param name="FacingOpenCall">In that spot, he called; <paramref name="FacingOpenThreeBet"/>: he re-raised.</param>
/// <param name="OpenToBigBlinds">The hero opened (raise first in): his raise-to in big blinds, blinds included, antes not;
/// <paramref name="OpenAllIn"/> when that raise was all-in.</param>
/// <param name="FacingOpenToBigBlinds">Facing an open (see <paramref name="FacingOpenFrom"/>): the opener's raise-to in big
/// blinds; <paramref name="FacingOpenEffectiveStack"/>: the smaller of the two starting stacks, in big blinds.</param>
/// <param name="ThreeBetToBigBlinds">In that spot, he 3-bet: his raise-to in big blinds; <paramref name="ThreeBetAllIn"/>
/// when all-in.</param>
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
    decimal? AllInExpectedNetChips = null,
    bool FoldToCbetFlopOpportunity = false,
    bool FoldToCbetFlop = false,
    bool RaiseCbetFlop = false,
    bool CbetTurnOpportunity = false,
    bool CbetTurn = false,
    bool CheckRaiseFlopOpportunity = false,
    bool CheckRaiseFlop = false,
    bool WonWhenSawFlop = false,
    int PostflopAggressive = 0,
    int PostflopDecisions = 0,
    PokerPosition? FacingOpenFrom = null,
    bool FacingOpenCall = false,
    bool FacingOpenThreeBet = false,
    decimal? OpenToBigBlinds = null,
    bool OpenAllIn = false,
    decimal? FacingOpenToBigBlinds = null,
    decimal? FacingOpenEffectiveStack = null,
    decimal? ThreeBetToBigBlinds = null,
    bool ThreeBetAllIn = false,
    bool FoldToCbetTurnOpportunity = false,
    bool FoldToCbetTurn = false,
    bool DonkBetFlopOpportunity = false,
    bool DonkBetFlop = false,
    bool ProbeBetTurnOpportunity = false,
    bool ProbeBetTurn = false)
{
    /// <summary>Bump on any change of a definition below or in <see cref="HandAnalyzer"/>.</summary>
    /// <remarks>2: all-in EV. 3: postflop play (see PostflopPlay). 4: defence against an open (ADR-0018). 5: open and 3-bet sizes (ADR-0020). 6: fold to turn c-bet, donk bet, probe bet.</remarks>
    public const int Version = 6;

    /// <summary>Expected result of an all-in in big blinds, comparable across levels and tournaments.</summary>
    public decimal? AllInExpectedNetBigBlinds(long bigBlind) =>
        AllInExpectedNetChips is { } chips && bigBlind > 0 ? Math.Round(chips / bigBlind, 2) : null;
}
