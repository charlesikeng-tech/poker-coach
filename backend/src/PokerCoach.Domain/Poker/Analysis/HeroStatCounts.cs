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
    decimal NetBigBlinds)
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
            facts.NetBigBlinds);
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
            NetBigBlinds + other.NetBigBlinds);
    }
}
