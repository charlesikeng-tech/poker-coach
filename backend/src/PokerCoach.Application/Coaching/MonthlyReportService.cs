using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PokerCoach.Application.Leaks;
using PokerCoach.Application.Statistics;
using PokerCoach.Application.Tournaments;
using PokerCoach.Application.Training;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Application.Coaching;

/// <summary>The model's monthly report, after validation.</summary>
public sealed record MonthlyReportData(
    DateOnly Month,
    MonthlyReport Report,
    MonthlyReportFigures Figures);

public sealed record MonthlyReportFigures(
    int Hands,
    int Tournaments,
    decimal Profit,
    decimal? Roi,
    int CashHands,
    decimal? CashBbPer100,
    int LeakCount,
    int DrillAttempts,
    int DrillCorrect);

public sealed class MonthlyReportService(
    PerformanceService performance,
    CashPerformanceService cashPerformance,
    ILeakService leaks,
    ITrainingStore training,
    ICoachingReportStore reports,
    AiUsageGuard guard,
    TimeProvider time)
{
    internal const string Kind = "monthly-report";
    public const int MinHands = 50;
    internal const int Version = 1;

    public async Task<ReportView<MonthlyReportData>?> GetAsync(Guid userId, DateOnly? month, string language, CancellationToken cancellationToken)
    {
        var target = month ?? PreviousMonth(time.GetUtcNow());
        var facts = await MeasureAsync(userId, target, cancellationToken);
        if (facts is null) return null;

        var stored = await reports.FindAsync<MonthlyReportData>(userId, Kind, Subject(target), language, cancellationToken);
        return stored is null ? null : View(stored, Fingerprint(facts, language));
    }

    public async Task<ReportOutcome<MonthlyReportData>> GenerateAsync(Guid userId, DateOnly? month, string language, CancellationToken cancellationToken)
    {
        var target = month ?? PreviousMonth(time.GetUtcNow());
        var facts = await MeasureAsync(userId, target, cancellationToken);
        if (facts is null)
            return ReportOutcome<MonthlyReportData>.Fail(CoachingFailure.NotFound);

        if (facts.TotalHands < MinHands)
            return ReportOutcome<MonthlyReportData>.Fail(CoachingFailure.NotEnoughData);

        var fingerprint = Fingerprint(facts, language);
        var subject = Subject(target);
        var stored = await reports.FindAsync<MonthlyReportData>(userId, Kind, subject, language, cancellationToken);
        if (stored is not null && stored.Fingerprint == fingerprint)
            return ReportOutcome<MonthlyReportData>.Of(View(stored, fingerprint));

        var outcome = await guard.RunAsync(
            userId,
            AiTask.MonthlyReport,
            (model, ct) => model.ReviewMonthAsync(new MonthlyReportPrompt(language, BuildFacts(facts)), ct),
            cancellationToken);
        if (outcome.Answer is not { } answer)
            return ReportOutcome<MonthlyReportData>.Fail(outcome.Failure!.Value);

        var figures = new MonthlyReportFigures(
            facts.TotalHands,
            facts.Performance.Tournaments,
            facts.Performance.Profit,
            facts.Performance.Roi,
            facts.CashResults?.Hands ?? 0,
            facts.CashResults?.BigBlindsPer100,
            facts.LeakCount,
            facts.DrillAttempts,
            facts.DrillCorrect);

        var data = new MonthlyReportData(target, answer.Value, figures);
        var report = new StoredReport<MonthlyReportData>(data, fingerprint, answer.Usage.Model, time.GetUtcNow());
        await reports.SaveAsync(userId, Kind, subject, language, report, cancellationToken);
        return ReportOutcome<MonthlyReportData>.Of(View(report, fingerprint));
    }

    internal sealed record MonthFacts(
        DateOnly Month,
        int TotalHands,
        PerformanceFigures Performance,
        CashPerformanceFigures? CashResults,
        int LeakCount,
        int PreviousLeakCount,
        int DrillAttempts,
        int DrillCorrect);

    private async Task<MonthFacts?> MeasureAsync(Guid userId, DateOnly month, CancellationToken cancellationToken)
    {
        var start = new DateTimeOffset(month.Year, month.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddMonths(1);
        var previousStart = start.AddMonths(-1);

        var perf = await performance.GetAsync(userId, start, end, cancellationToken);
        var cashReport = await cashPerformance.GetAsync(userId, start, end, cancellationToken);
        var cashResults = cashReport.Totals.Hands > 0 ? cashReport.Totals : (CashPerformanceFigures?)null;

        var totalHands = perf.Totals.Tournaments > 0 || (cashResults?.Hands ?? 0) > 0
            ? perf.Totals.Entries + (cashResults?.Hands ?? 0)
            : 0;
        if (totalHands == 0) return null;

        var leakAnalysis = await leaks.GetAsync(userId, null, start, end, cancellationToken);
        var previousLeaks = await leaks.GetAsync(userId, null, previousStart, start, cancellationToken);

        var (drillAttempts, drillCorrect) = await training.CountSinceAsync(userId, start, end, cancellationToken);

        return new MonthFacts(
            month,
            totalHands,
            perf.Totals,
            cashResults,
            leakAnalysis.Report.Leaks.Count,
            previousLeaks.Report.Leaks.Count,
            drillAttempts,
            drillCorrect);
    }

    internal static string BuildFacts(MonthFacts facts)
    {
        string Pct(decimal ratio) => (ratio * 100m).ToString("0.#", CultureInfo.InvariantCulture) + " %";
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Monthly report for {facts.Month:yyyy-MM}.");
        text.AppendLine();

        if (facts.Performance.Tournaments > 0)
        {
            var r = facts.Performance;
            text.AppendLine(CultureInfo.InvariantCulture, $"Tournaments: {r.Tournaments} played, {r.Entries} entries, {r.PaidEntries} paid entries.");
            text.AppendLine(CultureInfo.InvariantCulture, $"Profit: {r.Profit.ToString("0.00", CultureInfo.InvariantCulture)} €, ROI: {(r.Roi is { } roi ? Pct(roi) : "unknown")}, ITM: {(r.ItmRate is { } itm ? Pct(itm) : "unknown")}.");
        }

        if (facts.CashResults is { } cash)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Cash game: {cash.Hands} hands, BB/100: {(cash.BigBlindsPer100?.ToString("0.00", CultureInfo.InvariantCulture) ?? "N/A")}, net: {(cash.NetMoney >= 0 ? "+" : string.Empty)}{cash.NetMoney.ToString("0.00", CultureInfo.InvariantCulture)} €.");
        }

        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Leaks detected this month: {facts.LeakCount}. Previous month: {facts.PreviousLeakCount}.");
        var leakDelta = facts.LeakCount - facts.PreviousLeakCount;
        text.AppendLine(leakDelta switch
        {
            < 0 => string.Create(CultureInfo.InvariantCulture, $"The player reduced their leaks by {-leakDelta} compared to last month."),
            > 0 => string.Create(CultureInfo.InvariantCulture, $"The player has {leakDelta} more leaks than last month."),
            _ => "Same number of leaks as last month.",
        });

        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Training drills this month: {facts.DrillAttempts} answers, {facts.DrillCorrect} correct.");

        text.AppendLine();
        text.AppendLine("A month of results is a small sample: focus on leak trends and training consistency, not the money.");

        return text.ToString();
    }

    internal static string Fingerprint(MonthFacts facts, string language)
    {
        var key = string.Join(
            '|',
            Version.ToString(CultureInfo.InvariantCulture),
            facts.Month.ToString("O", CultureInfo.InvariantCulture),
            facts.TotalHands.ToString(CultureInfo.InvariantCulture),
            facts.Performance.TournamentsWithResult.ToString(CultureInfo.InvariantCulture),
            (facts.CashResults?.Hands ?? 0).ToString(CultureInfo.InvariantCulture),
            facts.LeakCount.ToString(CultureInfo.InvariantCulture),
            (facts.DrillAttempts / 10).ToString(CultureInfo.InvariantCulture),
            language);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    internal static DateOnly PreviousMonth(DateTimeOffset now) =>
        new DateOnly(now.Year, now.Month, 1).AddMonths(-1);

    private static string Subject(DateOnly month) =>
        month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static ReportView<MonthlyReportData> View(StoredReport<MonthlyReportData> stored, string fingerprint) =>
        new(stored.Report, stored.Fingerprint != fingerprint, stored.Model, stored.CreatedAt);
}
