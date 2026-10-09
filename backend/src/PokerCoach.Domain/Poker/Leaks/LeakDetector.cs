using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Domain.Poker.Leaks;

public enum LeakDirection
{
    TooLow,
    TooHigh,
}

public enum LeakConfidence
{
    /// <summary>Even the most favourable reading of the sample is outside the range (95 % interval).</summary>
    Confirmed,

    /// <summary>The rate is outside the range but the sample still allows a normal rate: to watch.</summary>
    Possible,
}

/// <param name="IntervalLow">Lower bound of the 95 % Wilson interval of the rate.</param>
/// <param name="IntervalHigh">Upper bound of that interval.</param>
public sealed record Leak(
    LeakStat Stat,
    PokerPosition? Position,
    LeakDirection Direction,
    LeakConfidence Confidence,
    decimal Rate,
    int Made,
    int Opportunities,
    decimal IntervalLow,
    decimal IntervalHigh,
    ReferenceRange Range);

/// <summary>A statistic that could not be judged yet: too few opportunities.</summary>
public sealed record UnderSampled(LeakStat Stat, PokerPosition? Position, int Opportunities, int Needed);

public sealed record LeakReport(IReadOnlyList<Leak> Leaks, IReadOnlyList<UnderSampled> UnderSampled);

/// <summary>
/// Compares the hero's rates to reference ranges. A rate is judged only with enough opportunities, and a
/// leak is "confirmed" only when the 95 % confidence interval of the rate lies entirely outside the range:
/// variance alone is not called a leak.
/// </summary>
public static class LeakDetector
{
    /// <summary>Below this many opportunities the interval is too wide to say anything useful.</summary>
    public const int MinOpportunities = 30;

    private const double Z = 1.96;

    public static LeakReport Detect(
        HeroStatCounts overall,
        IReadOnlyDictionary<PokerPosition, HeroStatCounts> byPosition,
        IReadOnlyList<ReferenceRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(overall);
        ArgumentNullException.ThrowIfNull(byPosition);
        ArgumentNullException.ThrowIfNull(ranges);

        var leaks = new List<Leak>();
        var underSampled = new List<UnderSampled>();
        foreach (var range in ranges)
        {
            var counts = range.Position is { } position
                ? byPosition.GetValueOrDefault(position, HeroStatCounts.Zero)
                : overall;
            var (made, opportunities) = Sample(range.Stat, counts);
            if (opportunities < MinOpportunities)
            {
                underSampled.Add(new UnderSampled(range.Stat, range.Position, opportunities, MinOpportunities));
                continue;
            }

            var rate = (decimal)made / opportunities;

            if (rate >= range.Min && rate <= range.Max)
            {
                continue;
            }

            var (low, high) = Wilson(made, opportunities);
            var direction = rate < range.Min ? LeakDirection.TooLow : LeakDirection.TooHigh;
            var confirmed = direction == LeakDirection.TooLow ? high < range.Min : low > range.Max;
            leaks.Add(new Leak(
                range.Stat,
                range.Position,
                direction,
                confirmed ? LeakConfidence.Confirmed : LeakConfidence.Possible,
                Math.Round(rate, 4),
                made,
                opportunities,
                low,
                high,
                range));
        }

        // Most certain and furthest from the range first.
        var ordered = leaks
            .OrderBy(l => l.Confidence)
            .ThenByDescending(l => Distance(l))
            .ToList();
        return new LeakReport(ordered, underSampled);
    }

    private static (int Made, int Opportunities) Sample(LeakStat stat, HeroStatCounts c) => stat switch
    {
        LeakStat.Vpip => (c.Vpip, c.PreflopDecisions),
        LeakStat.Pfr => (c.Pfr, c.PreflopDecisions),
        // VPIP − PFR over the same hands = the share of hands played by calling only.
        LeakStat.VpipPfrGap => (c.Vpip - c.Pfr, c.PreflopDecisions),
        LeakStat.Rfi => (c.Rfi, c.RfiOpportunities),
        LeakStat.Limp => (c.Limp, c.RfiOpportunities),
        LeakStat.Steal => (c.Steal, c.StealOpportunities),
        LeakStat.ThreeBet => (c.ThreeBet, c.ThreeBetOpportunities),
        LeakStat.FoldToThreeBet => (c.FoldToThreeBet, c.FoldToThreeBetOpportunities),
        LeakStat.CbetFlop => (c.CbetFlop, c.CbetFlopOpportunities),
        LeakStat.WentToShowdown => (c.WentToShowdown, c.SawFlop),
        LeakStat.WonAtShowdown => (c.WonAtShowdown, c.WentToShowdown),
        LeakStat.FoldToCbetFlop => (c.FoldToCbetFlop, c.FoldToCbetFlopOpportunities),
        LeakStat.CbetTurn => (c.CbetTurn, c.CbetTurnOpportunities),
        LeakStat.CheckRaiseFlop => (c.CheckRaiseFlop, c.CheckRaiseFlopOpportunities),
        LeakStat.WonWhenSawFlop => (c.WonWhenSawFlop, c.SawFlop),
        LeakStat.PostflopAggression => (c.PostflopAggressive, c.PostflopDecisions),
        _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, null),
    };

    /// <summary>95 % Wilson score interval: honest at small samples and near 0 % or 100 %.</summary>
    internal static (decimal Low, decimal High) Wilson(int made, int n)
    {
        var p = (double)made / n;
        var z2 = Z * Z;
        var denominator = 1 + (z2 / n);
        var center = (p + (z2 / (2 * n))) / denominator;
        var half = Z * Math.Sqrt((p * (1 - p) / n) + (z2 / (4.0 * n * n))) / denominator;
        return ((decimal)Math.Round(Math.Max(0, center - half), 4), (decimal)Math.Round(Math.Min(1, center + half), 4));
    }

    private static decimal Distance(Leak leak) =>
        leak.Direction == LeakDirection.TooLow ? leak.Range.Min - leak.Rate : leak.Rate - leak.Range.Max;
}
