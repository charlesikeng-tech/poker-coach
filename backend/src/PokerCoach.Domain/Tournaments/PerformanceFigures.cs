namespace PokerCoach.Domain.Tournaments;

/// <summary>
/// Aggregate of tournament results. Money and rates cover known results only; tournaments without a
/// result are counted in <see cref="Tournaments"/> and nowhere else.
/// </summary>
/// <param name="Roi">Profit / buy-ins (0.12 = +12 %); null without buy-ins.</param>
/// <param name="RoiLow">Lower bound of the 95 % confidence interval on ROI; null with fewer than 10 results.</param>
/// <param name="RoiHigh">Upper bound of the 95 % confidence interval on ROI; null with fewer than 10 results.</param>
/// <param name="ItmRate">Paid entries / entries; null without entries.</param>
public sealed record PerformanceFigures(
    int Tournaments,
    int TournamentsWithResult,
    int Entries,
    int PaidEntries,
    decimal BuyIns,
    decimal Winnings,
    decimal Profit,
    decimal? Roi,
    decimal? RoiLow,
    decimal? RoiHigh,
    decimal? ItmRate)
{
    private const int MinResultsForInterval = 10;
    private const double Z95 = 1.96;

    public static PerformanceFigures Of(IReadOnlyCollection<TournamentResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var known = results.Where(r => r.Status == TournamentResultStatus.Known).ToList();
        var entries = known.Sum(r => r.Entries!.Value);
        var paid = known.Sum(r => r.PaidEntries ?? 0);
        var buyIns = known.Sum(r => r.TotalBuyIn!.Value);
        var winnings = known.Sum(r => r.PrizeWinnings!.Value + r.BountyWinnings!.Value);
        var profit = winnings - buyIns;
        var roi = buyIns == 0m ? null : (decimal?)Math.Round(profit / buyIns, 4);

        decimal? roiLow = null;
        decimal? roiHigh = null;
        if (known.Count >= MinResultsForInterval && buyIns > 0m)
        {
            // Per-tournament ROI: profit_i / buy_in_i, normalized so each tournament contributes equally.
            var perTournament = known
                .Where(r => r.TotalBuyIn > 0m)
                .Select(r => (double)(r.Profit!.Value / r.TotalBuyIn!.Value))
                .ToArray();
            if (perTournament.Length >= 2)
            {
                var n = perTournament.Length;
                var mean = perTournament.Average();
                var variance = perTournament.Sum(r => (r - mean) * (r - mean)) / (n - 1);
                var se = Math.Sqrt(variance / n);
                roiLow = Math.Round((decimal)(mean - Z95 * se), 4);
                roiHigh = Math.Round((decimal)(mean + Z95 * se), 4);
            }
        }

        return new PerformanceFigures(
            results.Count,
            known.Count,
            entries,
            paid,
            buyIns,
            winnings,
            profit,
            roi,
            roiLow,
            roiHigh,
            entries == 0 ? null : Math.Round((decimal)paid / entries, 4));
    }
}
