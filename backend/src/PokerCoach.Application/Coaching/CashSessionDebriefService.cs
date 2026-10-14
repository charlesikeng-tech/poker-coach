using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Application.Coaching;

/// <summary>A key moment in a cash session: hand with a large result in big blinds.</summary>
public sealed record CashDebriefMoment(
    string Ref,
    Guid HandId,
    long BigBlind,
    PokerPosition? Position,
    string? HeroCards,
    decimal StackInBigBlinds,
    decimal NetBigBlinds);

public sealed record CashDebriefReport(TournamentDebrief Debrief, IReadOnlyList<CashDebriefMoment> Moments);

/// <summary>
/// The coach's debrief of one cash session day (ADR-0022): reuses the tournament debrief model call
/// with cash-adapted facts. A "session" is one UTC day of play. Key moments are the hands with the
/// largest absolute result in big blinds.
/// </summary>
public sealed class CashSessionDebriefService(
    ICashPerformanceReadStore cashStore,
    ICoachingReportStore reports,
    AiUsageGuard guard,
    TimeProvider time)
{
    internal const string Kind = "cash-debrief";

    public const int MinHands = 20;

    internal const int Version = 1;

    private const int MaxKeyMoments = 6;

    /// <summary>The stored debrief, if any, and whether the session's facts changed since. Free.</summary>
    public async Task<ReportView<CashDebriefReport>?> GetAsync(Guid userId, DateOnly day, string language, CancellationToken cancellationToken)
    {
        var stored = await reports.FindAsync<CashDebriefReport>(userId, Kind, Subject(day), language, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        var hands = await LoadDay(userId, day, cancellationToken);
        return View(stored, Fingerprint(day, hands, language));
    }

    /// <summary>Writes the debrief, unless the stored one is still up to date (then it is returned, unpaid).</summary>
    public async Task<ReportOutcome<CashDebriefReport>> GenerateAsync(Guid userId, DateOnly day, string language, CancellationToken cancellationToken)
    {
        var hands = await LoadDay(userId, day, cancellationToken);
        if (hands.Count == 0)
        {
            return ReportOutcome<CashDebriefReport>.Fail(CoachingFailure.NotFound);
        }

        if (hands.Count < MinHands)
        {
            return ReportOutcome<CashDebriefReport>.Fail(CoachingFailure.NotEnoughData);
        }

        var fingerprint = Fingerprint(day, hands, language);
        var stored = await reports.FindAsync<CashDebriefReport>(userId, Kind, Subject(day), language, cancellationToken);
        if (stored is not null && stored.Fingerprint == fingerprint)
        {
            return ReportOutcome<CashDebriefReport>.Of(View(stored, fingerprint));
        }

        var moments = hands
            .OrderByDescending(h => Math.Abs(h.NetBigBlinds))
            .Take(MaxKeyMoments)
            .Select((h, i) => new CashDebriefMoment(
                $"M{i + 1}",
                h.HandId,
                h.BigBlind,
                h.Position,
                h.HeroCards,
                h.StackInBigBlinds,
                h.NetBigBlinds))
            .ToList();

        var outcome = await guard.RunAsync(
            userId,
            AiTask.CashDebrief,
            async (model, ct) =>
            {
                var narratedHands = (await reports.LoadHandsAsync(userId, moments.Select(m => m.HandId).ToList(), ct))
                    .ToDictionary(h => h.HandId);
                var prompt = new DebriefPrompt(
                    language,
                    Facts(day, hands, moments),
                    moments.Where(m => narratedHands.ContainsKey(m.HandId)).Select(m => (m.Ref, HandNarrator.Tell(narratedHands[m.HandId]))).ToList());
                return await model.DebriefTournamentAsync(prompt, ct);
            },
            cancellationToken);

        if (outcome.Answer is not { } answer)
        {
            return ReportOutcome<CashDebriefReport>.Fail(outcome.Failure!.Value);
        }

        var report = new StoredReport<CashDebriefReport>(
            new CashDebriefReport(Checked(answer.Value, moments), moments),
            fingerprint,
            answer.Usage.Model,
            time.GetUtcNow());
        await reports.SaveAsync(userId, Kind, Subject(day), language, report, cancellationToken);
        return ReportOutcome<CashDebriefReport>.Of(View(report, fingerprint));
    }

    internal static TournamentDebrief Checked(TournamentDebrief debrief, IReadOnlyList<CashDebriefMoment> moments)
    {
        var known = moments.Select(m => m.Ref).ToHashSet(StringComparer.Ordinal);
        var order = moments.Select((m, i) => (m.Ref, i)).ToDictionary(x => x.Ref, x => x.i, StringComparer.Ordinal);
        var notes = debrief.Moments
            .Where(n => known.Contains(n.Ref))
            .DistinctBy(n => n.Ref)
            .OrderBy(n => order[n.Ref])
            .ToList();
        return debrief with { Moments = notes };
    }

    internal static string Facts(DateOnly day, IReadOnlyList<CashHandSummary> hands, IReadOnlyList<CashDebriefMoment> moments)
    {
        var text = new StringBuilder();
        string Bb(decimal value) => value.ToString("0.#", CultureInfo.InvariantCulture) + " BB";

        var totalNetBb = hands.Sum(h => h.NetBigBlinds);
        var bb100 = hands.Count > 0 ? Math.Round(totalNetBb / hands.Count * 100m, 2) : 0m;
        var totalNetMoney = Math.Round(hands.Sum(h => h.NetBigBlinds * h.BigBlind) / 100m, 2);

        var stakes = hands
            .GroupBy(h => h.BigBlind)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key / 100m:0.##}€ BB ({g.Count()} hands)")
            .ToList();

        text.AppendLine("Cash game session:");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Date: {day:yyyy-MM-dd}.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Stakes: {string.Join(", ", stakes)}.");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Hands: {hands.Count}. Net result: {(totalNetBb >= 0 ? "+" : string.Empty)}{Bb(totalNetBb)} ({(totalNetMoney >= 0 ? "+" : string.Empty)}{totalNetMoney.ToString("0.00", CultureInfo.InvariantCulture)} €). BB/100: {bb100.ToString("0.00", CultureInfo.InvariantCulture)}.");

        var tableFormats = hands
            .GroupBy(h => h.MaxSeats <= 6 ? "6-max" : "full ring")
            .Select(g => $"{g.Key} ({g.Count()} hands)")
            .ToList();
        text.AppendLine(CultureInfo.InvariantCulture, $"- Format: {string.Join(", ", tableFormats)}.");

        text.AppendLine();
        text.AppendLine("This is a cash game session, not a tournament. There is no buy-in, no prize, no finish position. The player's goal is to maximize profit in big blinds (BB/100). Do not refer to tournament concepts.");

        text.AppendLine();
        text.AppendLine(moments.Count == 0
            ? "Key moments: no hand stood out."
            : "Key moments (hands with the biggest wins or losses), sorted by impact:");
        foreach (var m in moments)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- {m.Ref}: {m.BigBlind / 100m:0.##}€ BB table, stack {Bb(m.StackInBigBlinds)}, result {(m.NetBigBlinds >= 0 ? "+" : string.Empty)}{Bb(m.NetBigBlinds)}{(m.Position is { } pos ? $", position {HandNarrator.Label(pos)}" : string.Empty)}{(m.HeroCards is { } cards ? $", holding {cards}" : string.Empty)}.");
        }

        return text.ToString();
    }

    internal static string Fingerprint(DateOnly day, IReadOnlyList<CashHandSummary> hands, string language)
    {
        var totalNetBb = hands.Sum(h => h.NetBigBlinds);
        var key = string.Join(
            '|',
            Version.ToString(CultureInfo.InvariantCulture),
            day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            hands.Count.ToString(CultureInfo.InvariantCulture),
            totalNetBb.ToString(CultureInfo.InvariantCulture),
            HeroHandFacts.Version.ToString(CultureInfo.InvariantCulture),
            language);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    private async Task<IReadOnlyList<CashHandSummary>> LoadDay(Guid userId, DateOnly day, CancellationToken cancellationToken)
    {
        var from = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to = from.AddDays(1);
        return await cashStore.ListCashHandSummariesAsync(userId, from, to, HeroHandFacts.Version, cancellationToken);
    }

    private static ReportView<CashDebriefReport> View(StoredReport<CashDebriefReport> stored, string fingerprint) =>
        new(stored.Report, stored.Fingerprint != fingerprint, stored.Model, stored.CreatedAt);

    private static string Subject(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
