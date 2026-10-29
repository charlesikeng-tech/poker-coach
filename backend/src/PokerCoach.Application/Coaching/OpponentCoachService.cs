using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PokerCoach.Application.Opponents;
using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Application.Coaching;

public sealed class OpponentCoachService(
    OpponentService opponents,
    ICoachingReportStore reports,
    AiUsageGuard guard,
    TimeProvider time)
{
    internal const string Kind = "opponent-coaching";
    public const int MinHands = 20;
    internal const int Version = 1;

    public async Task<ReportView<OpponentCoachingAdvice>?> GetAsync(Guid userId, Guid opponentId, string language, CancellationToken cancellationToken)
    {
        var detail = await opponents.GetAsync(userId, opponentId, cancellationToken);
        if (detail is null) return null;

        var subject = Subject(opponentId);
        var stored = await reports.FindAsync<OpponentCoachingAdvice>(userId, Kind, subject, language, cancellationToken);
        if (stored is null) return null;

        return View(stored, Fingerprint(detail, language));
    }

    public async Task<ReportOutcome<OpponentCoachingAdvice>> GenerateAsync(Guid userId, Guid opponentId, string language, CancellationToken cancellationToken)
    {
        var detail = await opponents.GetAsync(userId, opponentId, cancellationToken);
        if (detail is null)
            return ReportOutcome<OpponentCoachingAdvice>.Fail(CoachingFailure.NotFound);

        if (detail.Stats.Hands < MinHands)
            return ReportOutcome<OpponentCoachingAdvice>.Fail(CoachingFailure.NotEnoughData);

        var subject = Subject(opponentId);
        var fingerprint = Fingerprint(detail, language);
        var stored = await reports.FindAsync<OpponentCoachingAdvice>(userId, Kind, subject, language, cancellationToken);
        if (stored is not null && stored.Fingerprint == fingerprint)
            return ReportOutcome<OpponentCoachingAdvice>.Of(View(stored, fingerprint));

        var facts = BuildFacts(detail);
        var outcome = await guard.RunAsync(
            userId,
            AiTask.OpponentCoaching,
            (model, ct) => model.CoachAgainstOpponentAsync(new OpponentCoachingPrompt(language, facts), ct),
            cancellationToken);
        if (outcome.Answer is not { } answer)
            return ReportOutcome<OpponentCoachingAdvice>.Fail(outcome.Failure!.Value);

        var report = new StoredReport<OpponentCoachingAdvice>(answer.Value, fingerprint, answer.Usage.Model, time.GetUtcNow());
        await reports.SaveAsync(userId, Kind, subject, language, report, cancellationToken);
        return ReportOutcome<OpponentCoachingAdvice>.Of(View(report, fingerprint));
    }

    internal static string BuildFacts(OpponentDetail detail)
    {
        string Pct(decimal? ratio) => ratio is { } r ? (r * 100m).ToString("0.#", CultureInfo.InvariantCulture) + " %" : "N/A";
        var s = detail.Stats;
        var type = detail.Type;

        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Opponent scouting report — {s.Hands} hands across {detail.DistinctTournaments} tournament(s).");
        text.AppendLine(CultureInfo.InvariantCulture, $"Classification: {type} (deterministic, based on thresholds).");
        text.AppendLine();

        text.AppendLine("Preflop statistics:");
        text.AppendLine(CultureInfo.InvariantCulture, $"  VPIP: {Pct(s.VpipRate)} ({s.Vpip}/{s.Hands})");
        text.AppendLine(CultureInfo.InvariantCulture, $"  PFR: {Pct(s.PfrRate)} ({s.Pfr}/{s.Hands})");
        text.AppendLine(CultureInfo.InvariantCulture, $"  VPIP-PFR gap: {Pct(s.VpipPfrGap)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"  RFI: {Pct(s.RfiRate)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"  Steal: {Pct(s.StealRate)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"  3-Bet: {Pct(s.ThreeBetRate)} ({s.ThreeBet}/{s.ThreeBetOpportunity})");
        text.AppendLine(CultureInfo.InvariantCulture, $"  Fold to 3-bet: {Pct(s.FoldToThreeBetRate)}");
        text.AppendLine();

        text.AppendLine("Postflop statistics:");
        text.AppendLine(CultureInfo.InvariantCulture, $"  C-bet flop: {Pct(s.CbetFlopRate)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"  Fold to c-bet flop: {Pct(s.FoldToCbetFlopRate)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"  Postflop aggression: {Pct(s.AggressionFrequency)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"  WTSD: {Pct(s.WtsdRate)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"  W$SD: {Pct(s.WsdRate)}");

        // Append the deterministic exploit tips for context.
        var exploit = ExploitAdjustment.For(type);
        if (exploit.Tips.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"Deterministic exploit tips (for reference, based on archetype {type}):");
            text.AppendLine(CultureInfo.InvariantCulture, $"  Value bet sizing tendency: {exploit.ValueBetSizing}");
            text.AppendLine(CultureInfo.InvariantCulture, $"  Bluff frequency tendency: {exploit.BluffFrequency}");
            foreach (var tip in exploit.Tips)
                text.AppendLine(CultureInfo.InvariantCulture, $"  - {tip.Key} ({tip.Category})");
        }

        return text.ToString();
    }

    internal static string Fingerprint(OpponentDetail detail, string language)
    {
        var s = detail.Stats;
        var key = string.Join(
            '|',
            Version.ToString(CultureInfo.InvariantCulture),
            s.Hands.ToString(CultureInfo.InvariantCulture),
            Round(s.VpipRate), Round(s.PfrRate), Round(s.ThreeBetRate),
            Round(s.AggressionFrequency), Round(s.WtsdRate),
            detail.Type.ToString(),
            language);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    private static string Round(decimal? rate) =>
        rate is { } r ? Math.Round(r * 100m).ToString(CultureInfo.InvariantCulture) : "-";

    private static string Subject(Guid opponentId) => $"opponent:{opponentId:N}";

    private static ReportView<OpponentCoachingAdvice> View(StoredReport<OpponentCoachingAdvice> stored, string fingerprint) =>
        new(stored.Report, stored.Fingerprint != fingerprint, stored.Model, stored.CreatedAt);
}
