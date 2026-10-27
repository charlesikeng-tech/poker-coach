namespace PokerCoach.Domain.Poker.Analysis;

/// <summary>Opponent archetype derived from aggregate stats. Values are persisted: never renumber.</summary>
public enum PlayerType
{
    Unknown = 0,
    Nit = 1,
    Tag = 2,
    Lag = 3,
    CallingStation = 4,
    Maniac = 5,
    Shark = 6,
}

/// <summary>Aggregate stats for one opponent, suitable for classification.</summary>
public sealed record OpponentStats(
    int Hands,
    int Vpip,
    int Pfr,
    int RfiOpportunity,
    int Rfi,
    int StealOpportunity,
    int Steal,
    int ThreeBetOpportunity,
    int ThreeBet,
    int FoldToThreeBetOpportunity,
    int FoldToThreeBet,
    int SawFlop,
    int CbetFlopOpportunity,
    int CbetFlop,
    int FoldToCbetFlopOpportunity,
    int FoldToCbetFlop,
    int WentToShowdown,
    int WonAtShowdown,
    int PostflopAggressive,
    int PostflopDecisions)
{
    public const int MinHandsForClassification = 20;

    public decimal VpipRate => Hands > 0 ? (decimal)Vpip / Hands : 0;
    public decimal PfrRate => Hands > 0 ? (decimal)Pfr / Hands : 0;
    public decimal VpipPfrGap => VpipRate - PfrRate;
    public decimal? RfiRate => RfiOpportunity > 0 ? (decimal)Rfi / RfiOpportunity : null;
    public decimal? StealRate => StealOpportunity > 0 ? (decimal)Steal / StealOpportunity : null;
    public decimal ThreeBetRate => ThreeBetOpportunity > 0 ? (decimal)ThreeBet / ThreeBetOpportunity : 0;
    public decimal? FoldToThreeBetRate => FoldToThreeBetOpportunity > 0 ? (decimal)FoldToThreeBet / FoldToThreeBetOpportunity : null;
    public decimal? CbetFlopRate => CbetFlopOpportunity > 0 ? (decimal)CbetFlop / CbetFlopOpportunity : null;
    public decimal? FoldToCbetFlopRate => FoldToCbetFlopOpportunity > 0 ? (decimal)FoldToCbetFlop / FoldToCbetFlopOpportunity : null;
    public decimal AggressionFrequency => PostflopDecisions > 0 ? (decimal)PostflopAggressive / PostflopDecisions : 0;
    public decimal WtsdRate => SawFlop > 0 ? (decimal)WentToShowdown / SawFlop : 0;
    public decimal? WsdRate => WentToShowdown > 0 ? (decimal)WonAtShowdown / WentToShowdown : null;

    /// <summary>
    /// Deterministic classification by thresholds. Order matters: maniac before LAG (superset),
    /// calling station before nit (VPIP overlap ruled out by gap), shark last (tightest criteria).
    /// </summary>
    public PlayerType Classify()
    {
        if (Hands < MinHandsForClassification) return PlayerType.Unknown;

        var vpip = VpipRate;
        var gap = VpipPfrGap;
        var pfrR = PfrRate;
        var af = AggressionFrequency;

        if (vpip > 0.40m && pfrR > 0.30m) return PlayerType.Maniac;
        if (vpip > 0.25m && gap > 0.12m) return PlayerType.CallingStation;
        if (vpip > 0.25m && gap <= 0.10m) return PlayerType.Lag;
        if (vpip < 0.15m) return PlayerType.Nit;
        if (vpip is >= 0.18m and <= 0.28m && gap <= 0.06m && af > 0.40m) return PlayerType.Shark;
        if (vpip is >= 0.15m and <= 0.25m && gap <= 0.08m) return PlayerType.Tag;

        return PlayerType.Unknown;
    }
}
