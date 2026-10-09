namespace PokerCoach.Domain.Tournaments;

/// <summary>
/// Aggregate of tournament results. Money and rates cover known results only; tournaments without a
/// result are counted in <see cref="Tournaments"/> and nowhere else.
/// </summary>
/// <param name="Roi">Profit / buy-ins (0.12 = +12 %); null without buy-ins.</param>
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
    decimal? ItmRate)
{
    public static PerformanceFigures Of(IReadOnlyCollection<TournamentResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var known = results.Where(r => r.Status == TournamentResultStatus.Known).ToList();
        var entries = known.Sum(r => r.Entries!.Value);
        var paid = known.Sum(r => r.PaidEntries ?? 0);
        var buyIns = known.Sum(r => r.TotalBuyIn!.Value);
        var winnings = known.Sum(r => r.PrizeWinnings!.Value + r.BountyWinnings!.Value);
        var profit = winnings - buyIns;
        return new PerformanceFigures(
            results.Count,
            known.Count,
            entries,
            paid,
            buyIns,
            winnings,
            profit,
            buyIns == 0m ? null : Math.Round(profit / buyIns, 4),
            entries == 0 ? null : Math.Round((decimal)paid / entries, 4));
    }
}
