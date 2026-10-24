using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PokerCoach.Application.Leaks;
using PokerCoach.Application.Statistics;
using PokerCoach.Application.Tournaments;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Leaks;

namespace PokerCoach.Application.Coaching;

public sealed class StrategyPlanService(
    PerformanceService performance,
    CashPerformanceService cashPerformance,
    ILeakService leaks,
    ICoachingReportStore reports,
    AiUsageGuard guard,
    TimeProvider time)
{
    internal const string Kind = "strategy-plan";
    public const int MinTournaments = 30;
    public const int MinCashHands = 1000;
    internal const int Version = 1;

    public async Task<ReportView<StrategyPlanAdvice>?> GetAsync(Guid userId, GameType gameType, string language, CancellationToken cancellationToken)
    {
        var subject = Subject(gameType);
        if (gameType == GameType.CashGame)
        {
            var cashFacts = await MeasureCashAsync(userId, cancellationToken);
            if (cashFacts is null) return null;
            var stored = await reports.FindAsync<StrategyPlanAdvice>(userId, Kind, subject, language, cancellationToken);
            return stored is null ? null : View(stored, CashFingerprint(cashFacts, language));
        }
        else
        {
            var facts = await MeasureAsync(userId, cancellationToken);
            if (facts is null) return null;
            var stored = await reports.FindAsync<StrategyPlanAdvice>(userId, Kind, subject, language, cancellationToken);
            return stored is null ? null : View(stored, Fingerprint(facts, language));
        }
    }

    public async Task<ReportOutcome<StrategyPlanAdvice>> GenerateAsync(Guid userId, GameType gameType, string language, CancellationToken cancellationToken)
    {
        var subject = Subject(gameType);

        if (gameType == GameType.CashGame)
            return await GenerateCashAsync(userId, subject, language, cancellationToken);

        return await GenerateTournamentAsync(userId, subject, language, cancellationToken);
    }

    private async Task<ReportOutcome<StrategyPlanAdvice>> GenerateTournamentAsync(Guid userId, string subject, string language, CancellationToken cancellationToken)
    {
        var facts = await MeasureAsync(userId, cancellationToken);
        if (facts is null)
            return ReportOutcome<StrategyPlanAdvice>.Fail(CoachingFailure.NotFound);

        if (facts.TotalTournaments < MinTournaments)
            return ReportOutcome<StrategyPlanAdvice>.Fail(CoachingFailure.NotEnoughData);

        var fingerprint = Fingerprint(facts, language);
        var stored = await reports.FindAsync<StrategyPlanAdvice>(userId, Kind, subject, language, cancellationToken);
        if (stored is not null && stored.Fingerprint == fingerprint)
            return ReportOutcome<StrategyPlanAdvice>.Of(View(stored, fingerprint));

        var outcome = await guard.RunAsync(
            userId,
            AiTask.StrategyPlan,
            (model, ct) => model.AdviseStrategyPlanAsync(new StrategyPlanPrompt(language, BuildFacts(facts), GameType.Tournament), ct),
            cancellationToken);
        if (outcome.Answer is not { } answer)
            return ReportOutcome<StrategyPlanAdvice>.Fail(outcome.Failure!.Value);

        var report = new StoredReport<StrategyPlanAdvice>(answer.Value, fingerprint, answer.Usage.Model, time.GetUtcNow());
        await reports.SaveAsync(userId, Kind, subject, language, report, cancellationToken);
        return ReportOutcome<StrategyPlanAdvice>.Of(View(report, fingerprint));
    }

    private async Task<ReportOutcome<StrategyPlanAdvice>> GenerateCashAsync(Guid userId, string subject, string language, CancellationToken cancellationToken)
    {
        var facts = await MeasureCashAsync(userId, cancellationToken);
        if (facts is null)
            return ReportOutcome<StrategyPlanAdvice>.Fail(CoachingFailure.NotFound);

        if (facts.TotalHands < MinCashHands)
            return ReportOutcome<StrategyPlanAdvice>.Fail(CoachingFailure.NotEnoughData);

        var fingerprint = CashFingerprint(facts, language);
        var stored = await reports.FindAsync<StrategyPlanAdvice>(userId, Kind, subject, language, cancellationToken);
        if (stored is not null && stored.Fingerprint == fingerprint)
            return ReportOutcome<StrategyPlanAdvice>.Of(View(stored, fingerprint));

        var outcome = await guard.RunAsync(
            userId,
            AiTask.StrategyPlan,
            (model, ct) => model.AdviseStrategyPlanAsync(new StrategyPlanPrompt(language, BuildCashFacts(facts), GameType.CashGame), ct),
            cancellationToken);
        if (outcome.Answer is not { } answer)
            return ReportOutcome<StrategyPlanAdvice>.Fail(outcome.Failure!.Value);

        var report = new StoredReport<StrategyPlanAdvice>(answer.Value, fingerprint, answer.Usage.Model, time.GetUtcNow());
        await reports.SaveAsync(userId, Kind, subject, language, report, cancellationToken);
        return ReportOutcome<StrategyPlanAdvice>.Of(View(report, fingerprint));
    }

    internal sealed record StrategyFacts(
        int TotalTournaments,
        IReadOnlyList<PerformanceGroup> ByBuyIn,
        IReadOnlyList<PerformanceGroup> ByType,
        IReadOnlyList<PerformanceGroup> BySpeed,
        IReadOnlyList<Leak> ConfirmedLeaks,
        IReadOnlyList<Leak> PossibleLeaks);

    internal sealed record CashStrategyFacts(
        int TotalHands,
        IReadOnlyList<CashPerformanceGroup> ByStakes,
        IReadOnlyList<Leak> ConfirmedLeaks,
        IReadOnlyList<Leak> PossibleLeaks);

    private async Task<StrategyFacts?> MeasureAsync(Guid userId, CancellationToken cancellationToken)
    {
        var report = await performance.GetAsync(userId, from: null, to: null, cancellationToken);
        if (report.Totals.Tournaments == 0) return null;

        var leakAnalysis = await leaks.GetAsync(userId, format: null, from: null, to: null, cancellationToken);
        var confirmed = leakAnalysis.Report.Leaks.Where(l => l.Confidence == LeakConfidence.Confirmed).ToList();
        var possible = leakAnalysis.Report.Leaks.Where(l => l.Confidence == LeakConfidence.Possible).ToList();

        return new StrategyFacts(
            report.Totals.Tournaments,
            report.ByBuyIn,
            report.ByType,
            report.BySpeed,
            confirmed,
            possible);
    }

    private async Task<CashStrategyFacts?> MeasureCashAsync(Guid userId, CancellationToken cancellationToken)
    {
        var report = await cashPerformance.GetAsync(userId, from: null, to: null, cancellationToken);
        if (report.Totals.Hands == 0) return null;

        var leakAnalysis = await leaks.GetAsync(userId, format: null, from: null, to: null, cancellationToken, gameType: GameType.CashGame);
        var confirmed = leakAnalysis.Report.Leaks.Where(l => l.Confidence == LeakConfidence.Confirmed).ToList();
        var possible = leakAnalysis.Report.Leaks.Where(l => l.Confidence == LeakConfidence.Possible).ToList();

        return new CashStrategyFacts(
            report.Totals.Hands,
            report.ByStakes,
            confirmed,
            possible);
    }

    internal static string BuildFacts(StrategyFacts facts)
    {
        string Pct(decimal ratio) => (ratio * 100m).ToString("0.#", CultureInfo.InvariantCulture) + " %";
        string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture) + " €";
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Tournament strategy analysis — {facts.TotalTournaments} tournaments total (all history).");
        text.AppendLine();

        AppendTournamentGroup(text, "Performance by buy-in band:", facts.ByBuyIn, Pct, Money);
        AppendTournamentGroup(text, "Performance by tournament type:", facts.ByType, Pct, Money);
        AppendTournamentGroup(text, "Performance by speed:", facts.BySpeed, Pct, Money);

        AppendLeaks(text, facts.ConfirmedLeaks, facts.PossibleLeaks, Pct);

        text.AppendLine("Small samples (fewer than 20 tournaments in a group) are unreliable: mention the uncertainty.");
        return text.ToString();
    }

    internal static string BuildCashFacts(CashStrategyFacts facts)
    {
        string Bb100(decimal? value) => value is { } v ? v.ToString("0.00", CultureInfo.InvariantCulture) + " BB/100" : "N/A";
        string Bb(decimal value) => value.ToString("0.0", CultureInfo.InvariantCulture) + " BB";
        string Pct(decimal ratio) => (ratio * 100m).ToString("0.#", CultureInfo.InvariantCulture) + " %";
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Cash game strategy analysis — {facts.TotalHands} hands total (all history).");
        text.AppendLine();

        text.AppendLine("Performance by stake level:");
        foreach (var g in facts.ByStakes)
        {
            var f = g.Figures;
            text.AppendLine(CultureInfo.InvariantCulture,
                $"  {g.Key}: {f.Hands} hands, net {Bb(f.NetBigBlinds)}, BB/100 {Bb100(f.BigBlindsPer100)}, all-in adj BB/100 {Bb100(f.AllInAdjBigBlindsPer100)}");
        }

        text.AppendLine();

        AppendLeaks(text, facts.ConfirmedLeaks, facts.PossibleLeaks, Pct);

        text.AppendLine("Small samples (fewer than 500 hands at a stake) are unreliable: mention the uncertainty.");
        return text.ToString();
    }

    private static void AppendLeaks(StringBuilder text, IReadOnlyList<Leak> confirmed, IReadOnlyList<Leak> possible, Func<decimal, string> pct)
    {
        if (confirmed.Count > 0)
        {
            text.AppendLine("Confirmed leaks:");
            foreach (var l in confirmed)
                AppendLeak(text, l, pct);
            text.AppendLine();
        }

        if (possible.Count > 0)
        {
            text.AppendLine("Possible leaks:");
            foreach (var l in possible)
                AppendLeak(text, l, pct);
            text.AppendLine();
        }
    }

    private static void AppendLeak(StringBuilder text, Leak leak, Func<decimal, string> pct)
    {
        var position = leak.Position is { } p ? $" ({p})" : " (global)";
        var direction = leak.Direction == LeakDirection.TooHigh ? "too high" : "too low";
        text.AppendLine(CultureInfo.InvariantCulture,
            $"  {leak.Stat} {direction}{position}: rate {pct(leak.Rate)}, expected {pct(leak.Range.Min)}–{pct(leak.Range.Max)}, {leak.Opportunities} opportunities");
    }

    private static void AppendTournamentGroup(
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

    internal static string Fingerprint(StrategyFacts facts, string language)
    {
        var key = string.Join(
            '|',
            Version.ToString(CultureInfo.InvariantCulture),
            "tournament",
            facts.TotalTournaments.ToString(CultureInfo.InvariantCulture),
            facts.ByBuyIn.Count.ToString(CultureInfo.InvariantCulture),
            facts.ConfirmedLeaks.Count.ToString(CultureInfo.InvariantCulture),
            facts.PossibleLeaks.Count.ToString(CultureInfo.InvariantCulture),
            string.Join(',', facts.ByBuyIn.Select(g => g.Figures.TournamentsWithResult.ToString(CultureInfo.InvariantCulture))),
            language);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    internal static string CashFingerprint(CashStrategyFacts facts, string language)
    {
        var key = string.Join(
            '|',
            Version.ToString(CultureInfo.InvariantCulture),
            "cash",
            facts.TotalHands.ToString(CultureInfo.InvariantCulture),
            facts.ByStakes.Count.ToString(CultureInfo.InvariantCulture),
            facts.ConfirmedLeaks.Count.ToString(CultureInfo.InvariantCulture),
            facts.PossibleLeaks.Count.ToString(CultureInfo.InvariantCulture),
            string.Join(',', facts.ByStakes.Select(g => g.Figures.Hands.ToString(CultureInfo.InvariantCulture))),
            language);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    private static string Subject(GameType gameType) =>
        gameType == GameType.Tournament ? "strategy-plan" : $"strategy-plan:{gameType}";

    private static ReportView<StrategyPlanAdvice> View(StoredReport<StrategyPlanAdvice> stored, string fingerprint) =>
        new(stored.Report, stored.Fingerprint != fingerprint, stored.Model, stored.CreatedAt);
}
