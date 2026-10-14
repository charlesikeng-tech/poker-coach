using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Application.Statistics;

public sealed record CashHandRow(DateTimeOffset StartedAt, long BigBlind, decimal NetBigBlinds);

public sealed record CashPerformanceFigures(int Hands, decimal NetBigBlinds, decimal? BigBlindsPer100, decimal NetMoney)
{
    public static CashPerformanceFigures Of(int hands, decimal netBigBlinds, decimal netMoney) =>
        new(hands, Math.Round(netBigBlinds, 2), hands == 0 ? null : Math.Round(netBigBlinds / hands * 100m, 2), Math.Round(netMoney, 2));
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
    int PendingHands);

public interface ICashPerformanceReadStore
{
    Task<IReadOnlyList<CashHandRow>> ListCashHandsAsync(
        Guid userId, DateTimeOffset? from, DateTimeOffset? to, int factsVersion, CancellationToken cancellationToken);
}

public sealed class CashPerformanceService(ICashPerformanceReadStore store, IStatisticsReadStore stats)
{
    private static readonly string[] DayOrder =
        ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    private static readonly string[] TimeSlotOrder = ["morning", "afternoon", "evening", "night"];

    public async Task<CashPerformanceReport> GetAsync(
        Guid userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken, TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Utc;
        var rows = await store.ListCashHandsAsync(userId, from, to, HeroHandFacts.Version, cancellationToken);
        var pending = await stats.CountPendingAsync(userId, HeroHandFacts.Version, cancellationToken);

        var totalHands = rows.Count;
        var totalNetBb = rows.Sum(r => r.NetBigBlinds);
        var totalNetMoney = rows.Sum(r => r.NetBigBlinds * r.BigBlind) / 100m;
        var totals = CashPerformanceFigures.Of(totalHands, totalNetBb, totalNetMoney);

        return new CashPerformanceReport(
            totals,
            Curve(rows, zone),
            ByStakes(rows),
            ByMonth(rows, zone),
            ByDayOfWeek(rows, zone),
            ByTimeSlot(rows, zone),
            pending);
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
            rows.Sum(r => r.NetBigBlinds * r.BigBlind) / 100m));

    private static string TimeSlot(int hour) => hour switch
    {
        >= 6 and <= 11 => "morning",
        >= 12 and <= 17 => "afternoon",
        >= 18 and <= 22 => "evening",
        _ => "night",
    };
}
