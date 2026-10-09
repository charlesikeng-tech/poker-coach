using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Domain.Poker.Leaks;

/// <summary>Statistics a leak can be found on. Values travel in the API: never rename.</summary>
public enum LeakStat
{
    Vpip,
    Pfr,
    VpipPfrGap,
    Rfi,
    Limp,
    Steal,
    ThreeBet,
    FoldToThreeBet,
    CbetFlop,
    WentToShowdown,
    WonAtShowdown,
}

/// <summary>
/// The range a sound player's rate falls in, for one statistic and optionally one position. Rates are
/// ratios (0.22 = 22 %).
/// </summary>
public sealed record ReferenceRange(LeakStat Stat, PokerPosition? Position, decimal Min, decimal Max);

/// <summary>
/// Reference ranges, version 1 (ADR-0007): usual figures for solid regulars in low-stakes online MTTs
/// (€5–20, mostly 6-max), with 15+ big blinds. They are conventions to compare against, not truths;
/// they will become adjustable, then derived from the platform's players.
/// </summary>
public static class ReferenceRanges
{
    public const int Version = 1;

    /// <summary>Hands with fewer big blinds are push/fold poker: other references would apply.</summary>
    public const decimal MinStackBigBlinds = 15m;

    public static readonly IReadOnlyList<ReferenceRange> LowStakesMtt =
    [
        new(LeakStat.Vpip, null, 0.18m, 0.28m),
        new(LeakStat.Pfr, null, 0.14m, 0.23m),
        // Calling a lot more than raising: the passive-player leak, independent of the overall level.
        new(LeakStat.VpipPfrGap, null, 0.00m, 0.08m),
        new(LeakStat.Limp, null, 0.00m, 0.05m),
        new(LeakStat.Steal, null, 0.30m, 0.48m),
        new(LeakStat.ThreeBet, null, 0.05m, 0.11m),
        new(LeakStat.FoldToThreeBet, null, 0.40m, 0.62m),
        new(LeakStat.CbetFlop, null, 0.50m, 0.75m),
        new(LeakStat.WentToShowdown, null, 0.24m, 0.33m),
        new(LeakStat.WonAtShowdown, null, 0.48m, 0.60m),

        // Opening ranges widen towards the button.
        new(LeakStat.Rfi, PokerPosition.Utg, 0.11m, 0.20m),
        new(LeakStat.Rfi, PokerPosition.Utg1, 0.12m, 0.21m),
        new(LeakStat.Rfi, PokerPosition.Utg2, 0.13m, 0.22m),
        new(LeakStat.Rfi, PokerPosition.Lojack, 0.15m, 0.24m),
        new(LeakStat.Rfi, PokerPosition.Hijack, 0.18m, 0.28m),
        new(LeakStat.Rfi, PokerPosition.Cutoff, 0.24m, 0.36m),
        new(LeakStat.Rfi, PokerPosition.Button, 0.35m, 0.52m),
        new(LeakStat.Rfi, PokerPosition.SmallBlind, 0.28m, 0.50m),
    ];
}
