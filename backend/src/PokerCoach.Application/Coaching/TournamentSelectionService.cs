using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PokerCoach.Application.Tournaments;

namespace PokerCoach.Application.Coaching;

public sealed class TournamentSelectionService(
    PerformanceService performance,
    ICoachingReportStore reports,
    AiUsageGuard guard,
    TimeProvider time)
{
    internal const string Kind = "tournament-selection";
    public const int MinTournaments = 30;
    internal const int Version = 1;

    public async Task<ReportView<TournamentSelectionAdvice>?> GetAsync(Guid userId, string language, CancellationToken cancellationToken)
    {
        var facts = await MeasureAsync(userId, cancellationToken);
        if (facts is null) return null;

        var stored = await reports.FindAsync<TournamentSelectionAdvice>(userId, Kind, Subject(), language, cancellationToken);
        return stored is null ? null : View(stored, Fingerprint(facts, language));
    }

    public async Task<ReportOutcome<TournamentSelectionAdvice>> GenerateAsync(Guid userId, string language, CancellationToken cancellationToken)
    {
        var facts = await MeasureAsync(userId, cancellationToken);
        if (facts is null)
            return ReportOutcome<TournamentSelectionAdvice>.Fail(CoachingFailure.NotFound);

        if (facts.TotalTournaments < MinTournaments)
            return ReportOutcome<TournamentSelectionAdvice>.Fail(CoachingFailure.NotEnoughData);

        var fingerprint = Fingerprint(facts, language);
        var stored = await reports.FindAsync<TournamentSelectionAdvice>(userId, Kind, Subject(), language, cancellationToken);
        if (stored is not null && stored.Fingerprint == fingerprint)
            return ReportOutcome<TournamentSelectionAdvice>.Of(View(stored, fingerprint));

        var outcome = await guard.RunAsync(
            userId,
            AiTask.TournamentSelection,
            (model, ct) => model.AdviseTournamentSelectionAsync(new TournamentSelectionPrompt(language, BuildFacts(facts)), ct),
            cancellationToken);
        if (outcome.Answer is not { } answer)
            return ReportOutcome<TournamentSelectionAdvice>.Fail(outcome.Failure!.Value);

        var report = new StoredReport<TournamentSelectionAdvice>(answer.Value, fingerprint, answer.Usage.Model, time.GetUtcNow());
        await reports.SaveAsync(userId, Kind, Subject(), language, report, cancellationToken);
        return ReportOutcome<TournamentSelectionAdvice>.Of(View(report, fingerprint));
    }

    internal sealed record SelectionFacts(
        int TotalTournaments,
        IReadOnlyList<PerformanceGroup> ByBuyIn,
        IReadOnlyList<PerformanceGroup> ByType,
        IReadOnlyList<PerformanceGroup> BySpeed);

    private async Task<SelectionFacts?> MeasureAsync(Guid userId, CancellationToken cancellationToken)
    {
        var report = await performance.GetAsync(userId, from: null, to: null, cancellationToken);
        if (report.Totals.Tournaments == 0) return null;

        return new SelectionFacts(
            report.Totals.Tournaments,
            report.ByBuyIn,
            report.ByType,
            report.BySpeed);
    }

    internal static string BuildFacts(SelectionFacts facts)
    {
        string Pct(decimal ratio) => (ratio * 100m).ToString("0.#", CultureInfo.InvariantCulture) + " %";
        string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture) + " €";
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Tournament selection analysis — {facts.TotalTournaments} tournaments total (all history).");
        text.AppendLine();

        AppendGroup(text, "Performance by buy-in band:", facts.ByBuyIn, Pct, Money);
        AppendGroup(text, "Performance by tournament type:", facts.ByType, Pct, Money);
        AppendGroup(text, "Performance by speed:", facts.BySpeed, Pct, Money);

        text.AppendLine("Small samples (fewer than 20 tournaments in a group) are unreliable: mention the uncertainty.");
        return text.ToString();
    }

    private static void AppendGroup(
        StringBuilder text,
        string header,
        IReadOnlyList<PerformanceGroup> groups,
        Func<decimal, string> pct,
        Func<decimal, string> money)
    {
        text.AppendLine(header);
        foreach (var g in groups)
        {
            var f = g.Figures;
            var label = g.Key ?? "unknown";
            text.AppendLine(CultureInfo.InvariantCulture,
                $"  {label}: {f.Tournaments} tournaments, profit {money(f.Profit)}, ROI {(f.Roi is { } roi ? pct(roi) : "N/A")}, ITM {(f.ItmRate is { } itm ? pct(itm) : "N/A")}");
        }

        text.AppendLine();
    }

    internal static string Fingerprint(SelectionFacts facts, string language)
    {
        var key = string.Join(
            '|',
            Version.ToString(CultureInfo.InvariantCulture),
            facts.TotalTournaments.ToString(CultureInfo.InvariantCulture),
            facts.ByBuyIn.Count.ToString(CultureInfo.InvariantCulture),
            facts.ByType.Count.ToString(CultureInfo.InvariantCulture),
            facts.BySpeed.Count.ToString(CultureInfo.InvariantCulture),
            string.Join(',', facts.ByBuyIn.Select(g => g.Figures.TournamentsWithResult.ToString(CultureInfo.InvariantCulture))),
            language);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    private static string Subject() => "tournament-selection";

    private static ReportView<TournamentSelectionAdvice> View(StoredReport<TournamentSelectionAdvice> stored, string fingerprint) =>
        new(stored.Report, stored.Fingerprint != fingerprint, stored.Model, stored.CreatedAt);
}
