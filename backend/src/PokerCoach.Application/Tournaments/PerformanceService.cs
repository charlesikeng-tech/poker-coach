using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Application.Tournaments;

/// <summary>One point per tournament with a known result, in play order.</summary>
public sealed record ProfitPoint(int Index, DateTimeOffset? StartedAt, string Name, decimal Profit, decimal CumulativeProfit);

/// <param name="Key">Machine key of the group: a band name, or the provider's raw value; null when unknown.</param>
public sealed record PerformanceGroup(string? Key, PerformanceFigures Figures);

public sealed record PerformanceReport(
    PerformanceFigures Totals,
    IReadOnlyList<ProfitPoint> Curve,
    IReadOnlyList<PerformanceGroup> ByBuyIn,
    IReadOnlyList<PerformanceGroup> ByType,
    IReadOnlyList<PerformanceGroup> BySpeed);

/// <summary>Performance screen: cumulative profit and the same figures split by buy-in and format.</summary>
public sealed class PerformanceService(ITournamentReadStore store)
{
    /// <summary>Disjoint bands on the price of one entry, fee included (upper bound inclusive).</summary>
    internal static readonly (string Key, decimal Max)[] BuyInBands =
    [
        ("upTo5", 5m),
        ("from5To10", 10m),
        ("from10To20", 20m),
        ("over20", decimal.MaxValue),
    ];

    public async Task<PerformanceReport> GetAsync(Guid userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var facts = await store.ListAsync(userId, from, to, cancellationToken);
        var rows = facts.Select(f => (Facts: f, Item: TournamentListService.ToItem(f))).ToList();

        return new PerformanceReport(
            PerformanceFigures.Of(rows.Select(r => r.Item.Result).ToList()),
            Curve(rows.Select(r => r.Item)),
            Group(rows, r => BuyInBand(r.Item.BuyIn), BuyInBands.Select(b => b.Key).Append(null)),
            Group(rows, r => r.Facts.Type, null),
            Group(rows, r => r.Facts.Speed, null));
    }

    internal static string? BuyInBand(decimal? buyIn) =>
        buyIn is { } value ? BuyInBands.First(b => value <= b.Max).Key : null;

    private static List<ProfitPoint> Curve(IEnumerable<TournamentListItem> items)
    {
        var points = new List<ProfitPoint>();
        var cumulative = 0m;
        // Oldest first; ties broken by name so the curve is stable between loads.
        foreach (var item in items
                     .Where(i => i.Result.Status == TournamentResultStatus.Known)
                     .OrderBy(i => i.StartedAt)
                     .ThenBy(i => i.Name, StringComparer.Ordinal))
        {
            cumulative += item.Result.Profit!.Value;
            points.Add(new ProfitPoint(points.Count + 1, item.StartedAt, item.Name, item.Result.Profit.Value, cumulative));
        }

        return points;
    }

    /// <param name="order">Fixed group order (bands); null orders by number of tournaments, unknown last.</param>
    private static List<PerformanceGroup> Group(
        List<(TournamentFacts Facts, TournamentListItem Item)> rows,
        Func<(TournamentFacts Facts, TournamentListItem Item), string?> key,
        IEnumerable<string?>? order)
    {
        var groups = rows
            .GroupBy(key)
            .Select(g => new PerformanceGroup(g.Key, PerformanceFigures.Of(g.Select(r => r.Item.Result).ToList())))
            .ToList();

        if (order is not null)
        {
            var rank = order.Select((k, i) => (k, i)).ToDictionary(x => x.k ?? string.Empty, x => x.i);
            return groups.OrderBy(g => rank[g.Key ?? string.Empty]).ToList();
        }

        return groups
            .OrderBy(g => g.Key is null)
            .ThenByDescending(g => g.Figures.Tournaments)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
    }
}
