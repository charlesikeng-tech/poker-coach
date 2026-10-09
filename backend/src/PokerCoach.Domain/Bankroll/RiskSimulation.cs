namespace PokerCoach.Domain.Bankroll;

/// <summary>ROI with how uncertain it is: in MTTs a few hundred tournaments say little.</summary>
/// <param name="Mean">Average profit per tournament over its cost (0.1 = +10 %).</param>
/// <param name="Low">Lower bound of the 95 % interval.</param>
/// <param name="High">Upper bound of the 95 % interval.</param>
public sealed record RoiEstimate(int Tournaments, decimal Mean, decimal Low, decimal High);

/// <param name="Tournaments">Tournaments simulated ahead.</param>
/// <param name="AverageBuyIn">Cost per tournament used (the player's recent average).</param>
/// <param name="HalfLoss">Chance of losing half of the bankroll at some point.</param>
/// <param name="Ruin">Chance of losing it all.</param>
/// <param name="FinalLow">10th percentile of the bankroll at the end.</param>
/// <param name="FinalMedian">Median bankroll at the end.</param>
/// <param name="FinalHigh">90th percentile.</param>
/// <param name="TypicalDownswing">Median of the worst drop from a peak, in average buy-ins.</param>
public sealed record RiskOutlook(
    int Tournaments,
    decimal AverageBuyIn,
    decimal HalfLoss,
    decimal Ruin,
    decimal FinalLow,
    decimal FinalMedian,
    decimal FinalHigh,
    decimal TypicalDownswing);

/// <summary>
/// The future drawn from the player's own past: tournaments are resampled at random (bootstrap) from his
/// results, scaled to his current average buy-in, many times over. No distribution is assumed: MTT results
/// are mostly small losses and rare big wins, which a normal law would hide.
/// </summary>
public static class RiskSimulation
{
    /// <summary>Below this, the past is too thin to draw a future from.</summary>
    public const int MinTournaments = 50;

    public const int Paths = 2000;

    public static RoiEstimate Roi(IReadOnlyList<TournamentMoney> tournaments)
    {
        ArgumentNullException.ThrowIfNull(tournaments);
        var returns = tournaments.Where(t => t.Cost > 0).Select(t => (double)(t.Profit / t.Cost)).ToList();
        if (returns.Count == 0)
        {
            return new RoiEstimate(0, 0m, 0m, 0m);
        }

        // ROI as total profit over total cost; its spread from the per-tournament returns.
        var mean = (double)(tournaments.Where(t => t.Cost > 0).Sum(t => t.Profit) / tournaments.Where(t => t.Cost > 0).Sum(t => t.Cost));
        var average = returns.Average();
        var variance = returns.Count > 1 ? returns.Sum(r => (r - average) * (r - average)) / (returns.Count - 1) : 0;
        var margin = 1.96 * Math.Sqrt(variance / returns.Count);
        return new RoiEstimate(returns.Count, Round(mean), Round(mean - margin), Round(mean + margin));
    }

    /// <returns>Null when there are fewer than <see cref="MinTournaments"/> results or no bankroll left.</returns>
    public static RiskOutlook? Simulate(
        Random random,
        IReadOnlyList<TournamentMoney> tournaments,
        decimal balance,
        decimal averageBuyIn,
        int ahead = 500)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(tournaments);

        // Each result as a multiple of what the tournament cost, then replayed at today's buy-in.
        var returns = tournaments.Where(t => t.Cost > 0).Select(t => (double)(t.Profit / t.Cost)).ToArray();
        if (returns.Length < MinTournaments || balance <= 0 || averageBuyIn <= 0)
        {
            return null;
        }

        var start = (double)balance;
        var stake = (double)averageBuyIn;
        var finals = new double[Paths];
        var drops = new double[Paths];
        int halfLoss = 0, ruin = 0;
        for (var path = 0; path < Paths; path++)
        {
            double money = start, peak = start, worst = 0;
            bool half = false, broke = false;
            for (var t = 0; t < ahead; t++)
            {
                // Cannot play what is no longer there: a busted bankroll stops.
                if (money < stake)
                {
                    broke = true;
                    break;
                }

                money += returns[random.Next(returns.Length)] * stake;
                peak = Math.Max(peak, money);
                worst = Math.Max(worst, peak - money);
                half |= money <= start / 2;
            }

            finals[path] = money;
            drops[path] = worst / stake;
            halfLoss += half || broke ? 1 : 0;
            ruin += broke ? 1 : 0;
        }

        Array.Sort(finals);
        Array.Sort(drops);
        return new RiskOutlook(
            ahead,
            averageBuyIn,
            Round((double)halfLoss / Paths),
            Round((double)ruin / Paths),
            Money(finals[Paths / 10]),
            Money(finals[Paths / 2]),
            Money(finals[Paths * 9 / 10]),
            Math.Round((decimal)drops[Paths / 2], 0));
    }

    private static decimal Round(double ratio) => Math.Round((decimal)ratio, 4);

    private static decimal Money(double value) => Math.Round((decimal)value, 2);
}
