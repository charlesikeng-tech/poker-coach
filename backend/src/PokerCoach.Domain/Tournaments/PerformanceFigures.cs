namespace PokerCoach.Domain.Tournaments;

/// <summary>A tournament result paired with its buy-in component breakdown for aggregation.</summary>
/// <param name="TotalFees">Fee per entry × entries; null when the fee is unknown.</param>
/// <param name="TotalBountyBuyIns">Bounty buy-in per entry × entries; 0 for non-KO tournaments.</param>
/// <param name="TotalPrizePoolBuyIns">Prize-pool buy-in per entry × entries; null when the split is unknown.</param>
/// <param name="TotalRebuysCost">Rebuys × rebuy price + add-ons × add-on price.</param>
public sealed record TournamentResultWithBreakdown(
    TournamentResult Result,
    decimal? TotalFees,
    decimal TotalBountyBuyIns,
    decimal? TotalPrizePoolBuyIns,
    decimal TotalRebuysCost);

/// <summary>
/// Aggregate of tournament results. Money and rates cover known results only; tournaments without a
/// result are counted in <see cref="Tournaments"/> and nowhere else.
/// </summary>
/// <param name="Roi">Profit / buy-ins (0.12 = +12 %); null without buy-ins.</param>
/// <param name="RoiLow">Lower bound of the 95 % confidence interval on ROI; null with fewer than 10 results.</param>
/// <param name="RoiHigh">Upper bound of the 95 % confidence interval on ROI; null with fewer than 10 results.</param>
/// <param name="ItmRate">Paid entries / entries; null without entries.</param>
/// <param name="PrizeWinnings">Sum of prize winnings across known results.</param>
/// <param name="BountyWinnings">Sum of bounty winnings across known results.</param>
/// <param name="Fees">Sum of fees; null when the breakdown is unavailable for some results.</param>
/// <param name="BountyBuyIns">Sum of bounty buy-ins (0 when the player only plays non-KO).</param>
/// <param name="PrizePoolBuyIns">Sum of prize-pool buy-ins; null when the breakdown is unavailable for some results.</param>
/// <param name="RebuysCost">Sum of rebuy and add-on costs.</param>
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
    decimal? ItmRate,
    decimal PrizeWinnings,
    decimal BountyWinnings,
    decimal? Fees,
    decimal BountyBuyIns,
    decimal? PrizePoolBuyIns,
    decimal RebuysCost)
{
    private const int MinResultsForInterval = 10;

    public static PerformanceFigures Of(IReadOnlyCollection<TournamentResultWithBreakdown> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var known = results.Where(r => r.Result.Status == TournamentResultStatus.Known).ToList();
        var entries = known.Sum(r => r.Result.Entries!.Value);
        var paid = known.Sum(r => r.Result.PaidEntries ?? 0);
        var buyIns = known.Sum(r => r.Result.TotalBuyIn!.Value);
        var prizeWinnings = known.Sum(r => r.Result.PrizeWinnings!.Value);
        var bountyWinnings = known.Sum(r => r.Result.BountyWinnings!.Value);
        var winnings = prizeWinnings + bountyWinnings;
        var profit = winnings - buyIns;
        var roi = buyIns == 0m ? null : (decimal?)Math.Round(profit / buyIns, 4);

        var bountyBuyIns = known.Sum(r => r.TotalBountyBuyIns);
        var rebuysCost = known.Sum(r => r.TotalRebuysCost);
        var fees = known.All(r => r.TotalFees.HasValue)
            ? known.Sum(r => r.TotalFees!.Value)
            : (decimal?)null;
        var prizePoolBuyIns = known.All(r => r.TotalPrizePoolBuyIns.HasValue)
            ? known.Sum(r => r.TotalPrizePoolBuyIns!.Value)
            : (decimal?)null;

        // Same interval as the bankroll page (RoiInterval), shown from 10 results on.
        var interval = known.Count >= MinResultsForInterval
            ? RoiInterval.Of(known.Select(r => (r.Result.TotalBuyIn!.Value, r.Result.Profit!.Value)))
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
            entries == 0 ? null : Math.Round((decimal)paid / entries, 4),
            prizeWinnings,
            bountyWinnings,
            fees,
            bountyBuyIns,
            prizePoolBuyIns,
            rebuysCost);
    }
}
