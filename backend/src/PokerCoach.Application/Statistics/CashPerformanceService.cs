using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;


namespace PokerCoach.Application.Statistics;

public sealed record CashHandRow(DateTimeOffset StartedAt, long BigBlind, decimal NetBigBlinds, decimal? AllInExpectedNetChips, PokerRoom Room = default)
{
    public decimal AllInAdjNetBigBlinds =>
        AllInExpectedNetChips.HasValue && BigBlind > 0 ? AllInExpectedNetChips.Value / BigBlind : NetBigBlinds;
}

public sealed record CashPerformanceFigures(
    int Hands,
    decimal NetBigBlinds,
    decimal? BigBlindsPer100,
    decimal NetMoney,
    decimal? AllInAdjBigBlindsPer100,
    decimal? BigBlindsPer100Low,
    decimal? BigBlindsPer100High)
{
    public static CashPerformanceFigures Of(int hands, decimal netBigBlinds, decimal netMoney, decimal adjNetBigBlinds, decimal sumNetBbSquared = 0)
    {
        var ci = sumNetBbSquared != 0 ? Ci95(hands, netBigBlinds, sumNetBbSquared) : null;
        return new(
            hands,
            Math.Round(netBigBlinds, 2),
            hands == 0 ? null : Math.Round(netBigBlinds / hands * 100m, 2),
            Math.Round(netMoney, 2),
            hands == 0 ? null : Math.Round(adjNetBigBlinds / hands * 100m, 2),
            ci?.Low, ci?.High);
    }

    // 95 % CI on BB/100 using the sample standard deviation of per-hand BB results.
    private static (decimal Low, decimal High)? Ci95(int n, decimal netBb, decimal sumSq)
    {
        if (n < 30) return null;
        var mean = netBb / n;
        var sampleVar = (sumSq - (decimal)n * mean * mean) / (n - 1);
        if (sampleVar <= 0m) return null;
        var sdBb100 = (decimal)Math.Sqrt((double)sampleVar) * 100m / (decimal)Math.Sqrt(n);
        var margin = 1.96m * sdBb100;
        var bb100 = mean * 100m;
        return (Math.Round(bb100 - margin, 2), Math.Round(bb100 + margin, 2));
    }
}

public sealed record CashPerformanceGroup(string Key, CashPerformanceFigures Figures);

public sealed record CashProfitPoint(DateOnly Date, int Hands, decimal NetBigBlinds, decimal CumulativeNetBigBlinds, decimal NetMoney, decimal CumulativeNetMoney);

public sealed record CashPerformanceReport(
    CashPerformanceFigures Totals,
    IReadOnlyList<CashProfitPoint> Curve,
    IReadOnlyList<CashPerformanceGroup> ByStakes,
    IReadOnlyList<CashPerformanceGroup> ByMonth,
    IReadOnlyList<CashPerformanceGroup> ByDayOfWeek,
    IReadOnlyList<CashPerformanceGroup> ByTimeSlot,
    IReadOnlyList<PositionStatLine> ByPosition,
    int PendingHands,
    IReadOnlyList<PokerRoom> Rooms);

/// <summary>A cash hand with its id and net result, for selecting key moments.</summary>
public sealed record CashHandSummary(Guid HandId, DateTimeOffset StartedAt, long BigBlind, int MaxSeats, decimal NetBigBlinds, decimal StackInBigBlinds, PokerPosition? Position, string? HeroCards, decimal? AllInExpectedNetChips)
{
    public decimal AllInAdjNetBigBlinds =>
        AllInExpectedNetChips.HasValue && BigBlind > 0 ? AllInExpectedNetChips.Value / BigBlind : NetBigBlinds;
}

public interface ICashPerformanceReadStore
{
    Task<IReadOnlyList<CashHandRow>> ListCashHandsAsync(
        Guid userId, DateTimeOffset? from, DateTimeOffset? to, int factsVersion, CancellationToken cancellationToken, PokerRoom? room = null);

    Task<IReadOnlyList<CashHandSummary>> ListCashHandSummariesAsync(
        Guid userId, DateTimeOffset from, DateTimeOffset to, int factsVersion, CancellationToken cancellationToken);
}

public sealed class CashPerformanceService(ICashPerformanceReadStore store, IStatisticsReadStore stats)
{
    private static readonly string[] DayOrder =
        ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    private static readonly string[] TimeSlotOrder = ["morning", "afternoon", "evening", "night"];

    public async Task<CashPerformanceReport> GetAsync(
        Guid userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken, TimeZoneInfo? timeZone = null, PokerRoom? room = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Utc;
        var rows = await store.ListCashHandsAsync(userId, from, to, HeroHandFacts.Version, cancellationToken, room);
        var pending = await stats.CountPendingAsync(userId, HeroHandFacts.Version, cancellationToken);

        var totalHands = rows.Count;
        var totalNetBb = rows.Sum(r => r.NetBigBlinds);
        var totalNetMoney = rows.Sum(r => r.NetBigBlinds * r.BigBlind) / 100m;
        var totalAdjNetBb = rows.Sum(r => r.AllInAdjNetBigBlinds);
        var sumSq = rows.Sum(r => r.NetBigBlinds * r.NetBigBlinds);
        var totals = CashPerformanceFigures.Of(totalHands, totalNetBb, totalNetMoney, totalAdjNetBb, sumSq);

        var cashFilter = new StatisticsFilter(from, to, null, null, GameType: GameType.CashGame, Room: room);
        var positionCounts = await stats.CountByPositionAsync(userId, cashFilter, HeroHandFacts.Version, cancellationToken);
        var byPosition = positionCounts
            .OrderBy(r => r.Position.HasValue ? (int)r.Position.Value : int.MaxValue)
            .Select(r => new PositionStatLine(r.Position, StatLine.From(r.Counts)))
            .ToList();
        var rooms = rows.Select(r => r.Room).Distinct().OrderBy(r => r).ToList();

        return new CashPerformanceReport(
            totals,
            Curve(rows, zone),
            ByStakes(rows),
            ByMonth(rows, zone),
            ByDayOfWeek(rows, zone),
            ByTimeSlot(rows, zone),
            byPosition,
            pending,
            rooms);
    }

    private static List<CashProfitPoint> Curve(IReadOnlyList<CashHandRow> rows, TimeZoneInfo zone)
    {
        var byDate = rows
            .GroupBy(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.StartedAt, zone).DateTime))
            .OrderBy(g => g.Key)
            .ToList();

        var points = new List<CashProfitPoint>();
        var cumulativeBb = 0m;
        var cumulativeMoney = 0m;
        foreach (var day in byDate)
        {
            var hands = day.Count();
            var netBb = day.Sum(r => r.NetBigBlinds);
            var netMoney = day.Sum(r => r.NetBigBlinds * r.BigBlind) / 100m;
            cumulativeBb += netBb;
            cumulativeMoney += netMoney;
            points.Add(new CashProfitPoint(day.Key, hands, Math.Round(netBb, 2), Math.Round(cumulativeBb, 2), Math.Round(netMoney, 2), Math.Round(cumulativeMoney, 2)));
        }

        return points;
    }

    private static List<CashPerformanceGroup> ByStakes(IReadOnlyList<CashHandRow> rows) =>
        rows
            .GroupBy(r => r.BigBlind)
            .OrderBy(g => g.Key)
            .Select(g => GroupOf(g.Key.ToString(), g.ToList()))
            .ToList();

    private static List<CashPerformanceGroup> ByMonth(IReadOnlyList<CashHandRow> rows, TimeZoneInfo zone) =>
        rows
            .GroupBy(r =>
            {
                var local = TimeZoneInfo.ConvertTime(r.StartedAt, zone).DateTime;
                return new DateOnly(local.Year, local.Month, 1);
            })
            .OrderByDescending(g => g.Key)
            .Select(g => GroupOf(g.Key.ToString("yyyy-MM-dd"), g.ToList()))
            .ToList();

    private static List<CashPerformanceGroup> ByDayOfWeek(IReadOnlyList<CashHandRow> rows, TimeZoneInfo zone)
    {
        var grouped = rows
            .GroupBy(r => TimeZoneInfo.ConvertTime(r.StartedAt, zone).DateTime.DayOfWeek.ToString().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.ToList());
        return DayOrder
            .Where(grouped.ContainsKey)
            .Select(day => GroupOf(day, grouped[day]))
            .ToList();
    }

    private static List<CashPerformanceGroup> ByTimeSlot(IReadOnlyList<CashHandRow> rows, TimeZoneInfo zone)
    {
        var grouped = rows
            .GroupBy(r => TimeSlot(TimeZoneInfo.ConvertTime(r.StartedAt, zone).DateTime.Hour))
            .ToDictionary(g => g.Key, g => g.ToList());
        return TimeSlotOrder
            .Where(grouped.ContainsKey)
            .Select(slot => GroupOf(slot, grouped[slot]))
            .ToList();
    }

    private static CashPerformanceGroup GroupOf(string key, List<CashHandRow> rows) =>
        new(key, CashPerformanceFigures.Of(
            rows.Count,
            rows.Sum(r => r.NetBigBlinds),
            rows.Sum(r => r.NetBigBlinds * r.BigBlind) / 100m,
            rows.Sum(r => r.AllInAdjNetBigBlinds)));

    private static string TimeSlot(int hour) => hour switch
    {
        >= 6 and <= 11 => "morning",
        >= 12 and <= 17 => "afternoon",
        >= 18 and <= 22 => "evening",
        _ => "night",
    };
}
