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

        // Same interval as the bankroll page (RoiInterval), shown from 10 results on.
        var interval = known.Count >= MinResultsForInterval
            ? RoiInterval.Of(known.Select(r => (r.TotalBuyIn!.Value, r.Profit!.Value)))
            : null;

        return new PerformanceFigures(
            results.Count,
            known.Count,
            entries,
            paid,
            buyIns,
            winnings,
            profit,
            roi,
            interval?.Low,
            interval?.High,
            entries == 0 ? null : Math.Round((decimal)paid / entries, 4));
    }
}
