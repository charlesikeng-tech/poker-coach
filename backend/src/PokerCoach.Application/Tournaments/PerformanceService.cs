using PokerCoach.Domain.Poker;
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
    IReadOnlyList<PerformanceGroup> BySpeed,
    IReadOnlyList<PerformanceGroup> ByMonth,
    IReadOnlyList<PerformanceGroup> ByDayOfWeek,
    IReadOnlyList<PerformanceGroup> ByTimeSlot,
    IReadOnlyList<PokerRoom> Rooms);

/// <summary>Performance screen: cumulative profit and the same figures split by buy-in and format.</summary>
public sealed class PerformanceService(ITournamentReadStore store)
{
    /// <summary>
    /// Disjoint bands on the price of one entry, fee included (upper bound inclusive). Fine where the target
    /// plays (5–20 €), then 20–50 € to follow a move up towards mid stakes, and the rest.
    /// </summary>
    internal static readonly (string Key, decimal Max)[] BuyInBands =
    [
        ("upTo5", 5m),
        ("from5To10", 10m),
        ("from10To20", 20m),
        ("from20To50", 50m),
        ("over50", decimal.MaxValue),
    ];

    /// <param name="timeZone">The player's time zone: days and time slots are his, not UTC (a Sunday 00:30 start in
    /// Paris is a Saturday evening in UTC). Null: UTC.</param>
    public async Task<PerformanceReport> GetAsync(Guid userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken, TimeZoneInfo? timeZone = null, PokerRoom? room = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Utc;
        var facts = await store.ListAsync(userId, from, to, cancellationToken, room);
        var rows = facts.Select(f => (Facts: f, Item: TournamentListService.ToItem(f))).ToList();
        var rooms = await store.ListRoomsAsync(userId, from, to, cancellationToken);

        return new PerformanceReport(
            PerformanceFigures.Of(rows.Select(r => r.Item.Result).ToList()),
            Curve(rows.Select(r => r.Item)),
            Group(rows, r => BuyInBand(r.Item.BuyIn), BuyInBands.Select(b => b.Key).Append(null)),
            Group(rows, r => r.Facts.Type, null),
            Group(rows, r => r.Facts.Speed, null),
            GroupByMonth(rows, zone),
            GroupByDayOfWeek(rows, zone),
            GroupByTimeSlot(rows, zone),
            rooms);
    }

    private static readonly string[] DayOrder =
        ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    private static readonly string[] TimeSlotOrder = ["morning", "afternoon", "evening", "night"];

    private static DateTime Local(DateTimeOffset startedAt, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(startedAt, zone).DateTime;

    private static DateOnly MonthOf(DateTime local) => new(local.Year, local.Month, 1);

    private static List<PerformanceGroup> GroupByDayOfWeek(List<(TournamentFacts Facts, TournamentListItem Item)> rows, TimeZoneInfo zone)
    {
        var grouped = rows
            .Where(r => r.Facts.StartedAt is not null)
            .GroupBy(r => Local(r.Facts.StartedAt!.Value, zone).DayOfWeek.ToString().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g);
        return DayOrder
            .Where(day => grouped.ContainsKey(day))
            .Select(day => new PerformanceGroup(day, PerformanceFigures.Of(grouped[day].Select(r => r.Item.Result).ToList())))
            .ToList();
    }

    private static List<PerformanceGroup> GroupByTimeSlot(List<(TournamentFacts Facts, TournamentListItem Item)> rows, TimeZoneInfo zone)
    {
        var grouped = rows
            .Where(r => r.Facts.StartedAt is not null)
            .GroupBy(r => TimeSlot(Local(r.Facts.StartedAt!.Value, zone).Hour))
            .ToDictionary(g => g.Key, g => g);
        return TimeSlotOrder
            .Where(slot => grouped.ContainsKey(slot))
            .Select(slot => new PerformanceGroup(slot, PerformanceFigures.Of(grouped[slot].Select(r => r.Item.Result).ToList())))
            .ToList();
    }

    private static string TimeSlot(int hour) => hour switch
    {
        >= 6 and <= 11 => "morning",
        >= 12 and <= 17 => "afternoon",
        >= 18 and <= 22 => "evening",
        _ => "night",
    };

    private static List<PerformanceGroup> GroupByMonth(List<(TournamentFacts Facts, TournamentListItem Item)> rows, TimeZoneInfo zone) =>
        rows
            .Where(r => r.Facts.StartedAt is not null)
            .GroupBy(r => MonthOf(Local(r.Facts.StartedAt!.Value, zone)))
            .OrderByDescending(g => g.Key)
            .Select(g => new PerformanceGroup(g.Key.ToString("yyyy-MM-dd"), PerformanceFigures.Of(g.Select(r => r.Item.Result).ToList())))
            .ToList();

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
