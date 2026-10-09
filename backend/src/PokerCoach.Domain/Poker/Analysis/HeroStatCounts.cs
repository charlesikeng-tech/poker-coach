namespace PokerCoach.Domain.Poker.Analysis;

/// <summary>Sums of <see cref="HeroHandFacts"/> flags over a set of hands.</summary>
public sealed record HeroStatCounts(
    int Hands,
    int PreflopDecisions,
    int Vpip,
    int Pfr,
    int RfiOpportunities,
    int Rfi,
    int Limp,
    int StealOpportunities,
    int Steal,
    int ThreeBetOpportunities,
    int ThreeBet,
    int FoldToThreeBetOpportunities,
    int FoldToThreeBet,
    int SawFlop,
    int CbetFlopOpportunities,
    int CbetFlop,
    int WentToShowdown,
    int WonAtShowdown,
    decimal NetBigBlinds,
    int FoldToCbetFlopOpportunities = 0,
    int FoldToCbetFlop = 0,
    int RaiseCbetFlop = 0,
    int CbetTurnOpportunities = 0,
    int CbetTurn = 0,
    int CheckRaiseFlopOpportunities = 0,
    int CheckRaiseFlop = 0,
    int WonWhenSawFlop = 0,
    int PostflopAggressive = 0,
    int PostflopDecisions = 0)
{
    public static readonly HeroStatCounts Zero = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0m);

    /// <summary>The counts of a single hand.</summary>
    public static HeroStatCounts Of(HeroHandFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        static int One(bool flag) => flag ? 1 : 0;
        return new HeroStatCounts(
            1,
            One(facts.HadPreflopDecision),
            One(facts.Vpip),
            One(facts.Pfr),
            One(facts.RfiOpportunity),
            One(facts.Rfi),
            One(facts.Limp),
            One(facts.StealOpportunity),
            One(facts.Steal),
            One(facts.ThreeBetOpportunity),
            One(facts.ThreeBet),
            One(facts.FoldToThreeBetOpportunity),
            One(facts.FoldToThreeBet),
            One(facts.SawFlop),
            One(facts.CbetFlopOpportunity),
            One(facts.CbetFlop),
            One(facts.WentToShowdown),
            One(facts.WonAtShowdown),
            facts.NetBigBlinds,
            One(facts.FoldToCbetFlopOpportunity),
            One(facts.FoldToCbetFlop),
            One(facts.RaiseCbetFlop),
            One(facts.CbetTurnOpportunity),
            One(facts.CbetTurn),
            One(facts.CheckRaiseFlopOpportunity),
            One(facts.CheckRaiseFlop),
            One(facts.WonWhenSawFlop),
            facts.PostflopAggressive,
            facts.PostflopDecisions);
    }

    public HeroStatCounts Add(HeroStatCounts other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new HeroStatCounts(
            Hands + other.Hands,
            PreflopDecisions + other.PreflopDecisions,
            Vpip + other.Vpip,
            Pfr + other.Pfr,
            RfiOpportunities + other.RfiOpportunities,
            Rfi + other.Rfi,
            Limp + other.Limp,
            StealOpportunities + other.StealOpportunities,
            Steal + other.Steal,
            ThreeBetOpportunities + other.ThreeBetOpportunities,
            ThreeBet + other.ThreeBet,
            FoldToThreeBetOpportunities + other.FoldToThreeBetOpportunities,
            FoldToThreeBet + other.FoldToThreeBet,
            SawFlop + other.SawFlop,
            CbetFlopOpportunities + other.CbetFlopOpportunities,
            CbetFlop + other.CbetFlop,
            WentToShowdown + other.WentToShowdown,
            WonAtShowdown + other.WonAtShowdown,
            NetBigBlinds + other.NetBigBlinds,
            FoldToCbetFlopOpportunities + other.FoldToCbetFlopOpportunities,
            FoldToCbetFlop + other.FoldToCbetFlop,
            RaiseCbetFlop + other.RaiseCbetFlop,
            CbetTurnOpportunities + other.CbetTurnOpportunities,
            CbetTurn + other.CbetTurn,
            CheckRaiseFlopOpportunities + other.CheckRaiseFlopOpportunities,
            CheckRaiseFlop + other.CheckRaiseFlop,
            WonWhenSawFlop + other.WonWhenSawFlop,
            PostflopAggressive + other.PostflopAggressive,
            PostflopDecisions + other.PostflopDecisions);
    }
}
