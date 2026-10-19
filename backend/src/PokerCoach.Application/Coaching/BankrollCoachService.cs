using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PokerCoach.Application.Bankroll;
using PokerCoach.Domain.Bankroll;

namespace PokerCoach.Application.Coaching;

public enum BankrollCoachVerdict
{
    Stay,
    MoveUp,
    MoveDown,
    NotEnoughData,
}

/// <summary>The model's bankroll recommendation, after schema validation.</summary>
public sealed record BankrollAdvice(BankrollCoachVerdict Verdict, string Headline, string Reasoning);

/// <summary>
/// Analyses the player's bankroll figures and gives a stake-level recommendation (stay, move up,
/// move down). The report is cached by fingerprint: a new one is generated only when the key
/// figures change (ADR-0012).
/// </summary>
public sealed class BankrollCoachService(
    BankrollService bankroll,
    ICoachingReportStore reports,
    AiUsageGuard guard,
    TimeProvider time)
{
    internal const string Kind = "bankroll-coach";

    /// <summary>Below this many tournaments with results, the advice is too uncertain to sell.</summary>
    public const int MinTournaments = 30;

    /// <summary>Bump when the instructions change in a way the player would see.</summary>
    internal const int Version = 1;

    /// <summary>The stored advice, if any, and whether the key figures changed since. Free.</summary>
    public async Task<ReportView<BankrollAdvice>?> GetAsync(Guid userId, string language, CancellationToken cancellationToken)
    {
        var overview = await bankroll.GetAsync(userId, cancellationToken);
        if (overview is null)
        {
            return null;
        }

        var stored = await reports.FindAsync<BankrollAdvice>(userId, Kind, Subject(), language, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        return View(stored, Fingerprint(overview, language));
    }

    /// <summary>Generates a new advice, unless the stored one is still fresh. Credits charged only when the model is called.</summary>
    public async Task<ReportOutcome<BankrollAdvice>> GenerateAsync(Guid userId, string language, CancellationToken cancellationToken)
    {
        var overview = await bankroll.GetAsync(userId, cancellationToken);
        if (overview is null)
        {
            return ReportOutcome<BankrollAdvice>.Fail(CoachingFailure.NotFound);
        }

        if (overview.Roi.Tournaments < MinTournaments)
        {
            return ReportOutcome<BankrollAdvice>.Fail(CoachingFailure.NotEnoughData);
        }

        var fingerprint = Fingerprint(overview, language);
        var stored = await reports.FindAsync<BankrollAdvice>(userId, Kind, Subject(), language, cancellationToken);
        if (stored is not null && stored.Fingerprint == fingerprint)
        {
            return ReportOutcome<BankrollAdvice>.Of(View(stored, fingerprint));
        }

        var outcome = await guard.RunAsync(
            userId,
            AiTask.BankrollCoach,
            (model, ct) => model.AdviseOnBankrollAsync(new BankrollCoachPrompt(language, Facts(overview)), ct),
            cancellationToken);

        if (outcome.Answer is not { } answer)
        {
            return ReportOutcome<BankrollAdvice>.Fail(outcome.Failure!.Value);
        }

        var report = new StoredReport<BankrollAdvice>(answer.Value, fingerprint, answer.Usage.Model, time.GetUtcNow());
        await reports.SaveAsync(userId, Kind, Subject(), language, report, cancellationToken);
        return ReportOutcome<BankrollAdvice>.Of(View(report, fingerprint));
    }

    internal static string Facts(BankrollOverview overview)
    {
        var text = new StringBuilder();
        string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture) + " €";
        string Pct(decimal ratio) => (ratio * 100m).ToString("0.#", CultureInfo.InvariantCulture) + " %";

        text.AppendLine("Bankroll facts (amounts in euros):");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Balance: {Money(overview.Balance)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Buy-in rule: {overview.Settings.Rule} ({overview.Settings.BuyIns} buy-ins limit)");
        text.AppendLine(CultureInfo.InvariantCulture, $"Max average buy-in allowed: {Money(overview.Limit.MaxAverageBuyIn)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Tournaments played (since start date): {overview.Tournaments}");

        text.AppendLine();
        text.AppendLine("Buy-in compliance:");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Max average allowed: {Money(overview.Limit.MaxAverageBuyIn)}");
        if (overview.Limit.RecentAverageBuyIn is { } recent)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- Recent average buy-in (last 50 tournaments): {Money(recent)}");
        }
        else
        {
            text.AppendLine("- Recent average buy-in: no recent tournament");
        }

        if (overview.Limit.PlayedRecently > 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- Above limit recently (last 30 days): {overview.Limit.AboveLimitRecently} out of {overview.Limit.PlayedRecently} tournaments");
        }

        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Performance ({overview.Roi.Tournaments} tournaments in sample — all available history):");
        text.AppendLine(CultureInfo.InvariantCulture, $"- ROI mean: {Pct(overview.Roi.Mean)} (95% confidence interval: {Pct(overview.Roi.Low)} to {Pct(overview.Roi.High)})");
        if (overview.Roi.Tournaments < 100)
        {
            text.AppendLine("- Note: small sample, confidence interval is wide and the ROI is unreliable");
        }

        if (overview.Risk is { } risk)
        {
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"Risk simulation (500 tournaments at {Money(risk.AverageBuyIn)} average buy-in, 2000 runs):");
            text.AppendLine(CultureInfo.InvariantCulture, $"- Risk of ruin (losing everything): {Pct(risk.Ruin)}");
            text.AppendLine(CultureInfo.InvariantCulture, $"- Risk of losing half the bankroll: {Pct(risk.HalfLoss)}");
            text.AppendLine(CultureInfo.InvariantCulture, $"- Typical worst downswing from a peak: {risk.TypicalDownswing.ToString("0.#", CultureInfo.InvariantCulture)} buy-ins");
            text.AppendLine(CultureInfo.InvariantCulture, $"- Projected balance after those 500 tournaments: {Money(risk.FinalLow)} (10th pct) / {Money(risk.FinalMedian)} (50th pct) / {Money(risk.FinalHigh)} (90th pct)");
        }

        return text.ToString();
    }

    internal static string Fingerprint(BankrollOverview overview, string language)
    {
        var key = string.Join(
            '|',
            Version.ToString(CultureInfo.InvariantCulture),
            Math.Round(overview.Balance, 1).ToString(CultureInfo.InvariantCulture),
            overview.Roi.Tournaments.ToString(CultureInfo.InvariantCulture),
            Math.Round(overview.Roi.Mean, 4).ToString(CultureInfo.InvariantCulture),
            overview.Settings.Rule.ToString(),
            overview.Limit.MaxAverageBuyIn.ToString(CultureInfo.InvariantCulture),
            overview.Limit.RecentAverageBuyIn?.ToString(CultureInfo.InvariantCulture) ?? "-",
            language);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    private static ReportView<BankrollAdvice> View(StoredReport<BankrollAdvice> stored, string fingerprint) =>
        new(stored.Report, stored.Fingerprint != fingerprint, stored.Model, stored.CreatedAt);

    private static string Subject() => "bankroll";
}
