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
    FoldToCbetFlop,
    CbetTurn,
    CheckRaiseFlop,
    WonWhenSawFlop,
    PostflopAggression,
    FoldToCbetTurn,
    DonkBetFlop,
    ProbeBetTurn,
}

/// <summary>
/// The range a sound player's rate falls in, for one statistic and optionally one position. Rates are
/// ratios (0.22 = 22 %).
/// </summary>
public sealed record ReferenceRange(LeakStat Stat, PokerPosition? Position, decimal Min, decimal Max);

/// <summary>
/// Reference ranges, version 3 (ADR-0007; 2 adds the postflop statistics; 3 adds fold to turn c-bet, donk bet, probe bet): usual figures for solid regulars in low-stakes online MTTs
/// (€5–20, mostly 6-max), with 15+ big blinds. They are conventions to compare against, not truths;
/// they will become adjustable, then derived from the platform's players.
/// </summary>
public static class ReferenceRanges
{
    public const int Version = 3;

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

        // Postflop (version 2). Folding too often to c-bets is the classic exploitable leak at these stakes;
        // giving up on the turn and never check-raising make a player easy to bluff and easy to play against.
        new(LeakStat.FoldToCbetFlop, null, 0.35m, 0.55m),
        new(LeakStat.CbetTurn, null, 0.40m, 0.62m),
        new(LeakStat.CheckRaiseFlop, null, 0.05m, 0.15m),
        new(LeakStat.WonWhenSawFlop, null, 0.42m, 0.54m),
        // Bets and raises over bets, raises, calls and folds after the flop.
        new(LeakStat.PostflopAggression, null, 0.35m, 0.52m),
        // Folding the turn after calling the flop c-bet: the classic "call one and done" pattern.
        new(LeakStat.FoldToCbetTurn, null, 0.38m, 0.55m),
        // Betting into the preflop raiser: usually a fundamental mistake at these stakes.
        new(LeakStat.DonkBetFlop, null, 0.00m, 0.08m),
        // Betting when the PFR checks back the flop: punishing the missed c-bet.
        new(LeakStat.ProbeBetTurn, null, 0.25m, 0.40m),

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

    /// <summary>
    /// Usual figures for solid regulars in low-stakes online 6-max cash games (NL10–NL25, 100 BB deep).
    /// Deeper stacks and no ICM shift ranges compared to MTTs: wider opens, more 3-betting, lower c-bet
    /// frequencies, and more postflop play.
    /// </summary>
    public static readonly IReadOnlyList<ReferenceRange> LowStakesCash =
    [
        new(LeakStat.Vpip, null, 0.21m, 0.28m),
        new(LeakStat.Pfr, null, 0.17m, 0.24m),
        new(LeakStat.VpipPfrGap, null, 0.00m, 0.06m),
        new(LeakStat.Limp, null, 0.00m, 0.03m),
        new(LeakStat.Steal, null, 0.35m, 0.52m),
        new(LeakStat.ThreeBet, null, 0.07m, 0.12m),
        new(LeakStat.FoldToThreeBet, null, 0.45m, 0.60m),
        new(LeakStat.CbetFlop, null, 0.45m, 0.68m),
        new(LeakStat.WentToShowdown, null, 0.24m, 0.31m),
        new(LeakStat.WonAtShowdown, null, 0.50m, 0.58m),

        new(LeakStat.FoldToCbetFlop, null, 0.35m, 0.50m),
        new(LeakStat.CbetTurn, null, 0.40m, 0.58m),
        new(LeakStat.CheckRaiseFlop, null, 0.06m, 0.14m),
        new(LeakStat.WonWhenSawFlop, null, 0.44m, 0.54m),
        new(LeakStat.PostflopAggression, null, 0.38m, 0.52m),
        new(LeakStat.FoldToCbetTurn, null, 0.35m, 0.50m),
        new(LeakStat.DonkBetFlop, null, 0.00m, 0.05m),
        new(LeakStat.ProbeBetTurn, null, 0.28m, 0.42m),

        // 6-max cash RFI: wider than MTT at every seat (no ICM, deeper stacks).
        new(LeakStat.Rfi, PokerPosition.Utg, 0.15m, 0.22m),
        new(LeakStat.Rfi, PokerPosition.Utg1, 0.16m, 0.23m),
        new(LeakStat.Rfi, PokerPosition.Utg2, 0.17m, 0.24m),
        new(LeakStat.Rfi, PokerPosition.Lojack, 0.19m, 0.27m),
        new(LeakStat.Rfi, PokerPosition.Hijack, 0.22m, 0.30m),
        new(LeakStat.Rfi, PokerPosition.Cutoff, 0.27m, 0.38m),
        new(LeakStat.Rfi, PokerPosition.Button, 0.42m, 0.56m),
        new(LeakStat.Rfi, PokerPosition.SmallBlind, 0.32m, 0.52m),
    ];
}

public static class LeakStats
{
    /// <summary>Statistics of play after the flop (the advanced leaks of ADR-0016).</summary>
    public static bool IsPostflop(this LeakStat stat) => stat is LeakStat.CbetFlop
        or LeakStat.WentToShowdown
        or LeakStat.WonAtShowdown
        or LeakStat.FoldToCbetFlop
        or LeakStat.CbetTurn
        or LeakStat.CheckRaiseFlop
        or LeakStat.WonWhenSawFlop
        or LeakStat.PostflopAggression
        or LeakStat.FoldToCbetTurn
        or LeakStat.DonkBetFlop
        or LeakStat.ProbeBetTurn;
}
