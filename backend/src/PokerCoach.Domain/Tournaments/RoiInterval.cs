namespace PokerCoach.Domain.Tournaments;

/// <summary>ROI (total profit over total cost) and its 95 % interval.</summary>
public sealed record RoiRange(int Tournaments, decimal Roi, decimal Low, decimal High);

/// <summary>
/// The one interval on ROI shown anywhere (performance, bankroll), so the two pages never disagree.
/// ROI is a ratio of sums, so the interval is the ratio estimator's (delta method): it is centred on the ROI
/// shown, and a €20 tournament weighs four times a €5 one, as it does in the ROI. Averaging per-tournament
/// returns instead would centre the interval elsewhere as soon as buy-ins vary.
/// </summary>
public static class RoiInterval
{
    private const double Z95 = 1.96;

    /// <summary>Null without a paid tournament; the interval needs two of them to have a width.</summary>
    public static RoiRange? Of(IEnumerable<(decimal Cost, decimal Profit)> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var paid = results.Where(r => r.Cost > 0m).Select(r => ((double)r.Cost, (double)r.Profit)).ToList();
        if (paid.Count == 0)
        {
            return null;
        }

        var n = paid.Count;
        var cost = paid.Sum(r => r.Item1);
        var roi = paid.Sum(r => r.Item2) / cost;
        var margin = 0d;
        if (n > 1)
        {
            // Residual of each tournament against the overall ROI, scaled by the average cost.
            var residuals = paid.Sum(r => Math.Pow(r.Item2 - (roi * r.Item1), 2));
            margin = Z95 * Math.Sqrt(residuals / (n * (n - 1d))) / (cost / n);
        }

        return new RoiRange(n, Round(roi), Round(roi - margin), Round(roi + margin));
    }

    private static decimal Round(double value) => Math.Round((decimal)value, 4);
}
