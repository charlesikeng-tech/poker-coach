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
