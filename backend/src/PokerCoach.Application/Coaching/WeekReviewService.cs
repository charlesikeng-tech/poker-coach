using PokerCoach.Application.Subscriptions;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PokerCoach.Application.Progress;
using PokerCoach.Application.Tournaments;
using PokerCoach.Application.Training;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Progress;
using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Application.Coaching;

public sealed record PriorityNote(string Ref, string Note);

/// <summary>The model's review of a week of the plan, after validation (ADR-0012).</summary>
public sealed record WeekReview(string Headline, string Summary, IReadOnlyList<PriorityNote> Priorities, IReadOnlyList<string> NextSteps);

/// <summary>What the model is asked: the week's plan and measured progress, written by us.</summary>
public sealed record WeekReviewPrompt(string Language, string Facts);

/// <summary>A priority as measured, shown next to the coach's note: our figures, not the model's.</summary>
public sealed record ReviewedPriority(
    string Ref,
    LeakStat Stat,
    PokerPosition? Position,
    LeakDirection Direction,
    PriorityStatus Status,
    decimal? Rate,
    int Opportunities,
    decimal BaselineRate,
    decimal Min,
    decimal Max);

/// <param name="WeekOver">False while the week is being played: the review is a mid-week check.</param>
public sealed record WeekReviewReport(DateOnly WeekStart, bool WeekOver, WeekReview Review, IReadOnlyList<ReviewedPriority> Priorities);

/// <summary>
/// The coach's review of a week (ADR-0012): how the plan's priorities moved on the week's hands, what the
/// drills and results say, what to do next. Progress is measured by <see cref="ProgressService"/>; the
/// model only comments on it.
/// </summary>
public sealed class WeekReviewService(
    ProgressService progress,
    ITrainingStore training,
    PerformanceService performance,
    ICoachingReportStore reports,
    ICoachingStore ledger,
    ICoachingModel model,
    CoachingOptions options,
    PlanAccess plans,
    TimeProvider time)
{
    internal const string Kind = "week-review";

    /// <summary>Below this many hands in the week, there is nothing measured to comment on.</summary>
    public const int MinHands = 30;

    internal const int Version = 1;

    private readonly CoachingGate gate = new(ledger, model, options, plans, time);

    /// <summary>The stored review of the week (this week when null), and whether the week moved since. Free.</summary>
    public async Task<ReportView<WeekReviewReport>?> GetAsync(Guid userId, DateOnly? week, string language, CancellationToken cancellationToken)
    {
        var facts = await MeasureAsync(userId, week, cancellationToken);
        if (facts is null)
        {
            return null;
        }

        var stored = await reports.FindAsync<WeekReviewReport>(userId, Kind, Subject(facts.Week.Plan.WeekStart), language, cancellationToken);
        return stored is null ? null : View(stored, Fingerprint(facts, language));
    }

    public async Task<ReportOutcome<WeekReviewReport>> GenerateAsync(Guid userId, DateOnly? week, string language, CancellationToken cancellationToken)
    {
        var facts = await MeasureAsync(userId, week, cancellationToken);
        if (facts is null)
        {
            return ReportOutcome<WeekReviewReport>.Fail(CoachingFailure.NotFound);
        }

        if (facts.Week.Hands < MinHands)
        {
            return ReportOutcome<WeekReviewReport>.Fail(CoachingFailure.NotEnoughData);
        }

        var fingerprint = Fingerprint(facts, language);
        var subject = Subject(facts.Week.Plan.WeekStart);
        var stored = await reports.FindAsync<WeekReviewReport>(userId, Kind, subject, language, cancellationToken);
        if (stored is not null && stored.Fingerprint == fingerprint)
        {
            return ReportOutcome<WeekReviewReport>.Of(View(stored, fingerprint));
        }

        if (await gate.CheckAsync(userId, Kind, options.DailyWeekReviewsPerUser, freeMonthlyAllowance: 0, cancellationToken) is { } refused)
        {
            return ReportOutcome<WeekReviewReport>.Fail(refused);
        }

        var priorities = facts.Week.Priorities
            .Select((p, i) => new ReviewedPriority(
                $"P{i + 1}",
                p.Priority.Stat,
                p.Priority.Position,
                p.Priority.Direction,
                p.Week.Status,
                p.Week.Rate,
                p.Week.Opportunities,
                p.Priority.BaselineRate,
                p.Priority.Min,
                p.Priority.Max))
            .ToList();

        ModelAnswer<WeekReview> answer;
        try
        {
            answer = await model.ReviewWeekAsync(new WeekReviewPrompt(language, Facts(facts, priorities)), cancellationToken);
        }
        catch (CoachingModelException)
        {
            return ReportOutcome<WeekReviewReport>.Fail(CoachingFailure.ModelFailed);
        }

        await gate.RecordAsync(userId, Kind, answer.Usage, cancellationToken);

        var known = priorities.Select(p => p.Ref).ToHashSet(StringComparer.Ordinal);
        var review = answer.Value with
        {
            Priorities = answer.Value.Priorities.Where(p => known.Contains(p.Ref)).DistinctBy(p => p.Ref).ToList(),
        };
        var report = new StoredReport<WeekReviewReport>(
            new WeekReviewReport(facts.Week.Plan.WeekStart, facts.WeekOver, review, priorities),
            fingerprint,
            answer.Usage.Model,
            time.GetUtcNow());
        await reports.SaveAsync(userId, Kind, subject, language, report, cancellationToken);
        return ReportOutcome<WeekReviewReport>.Of(View(report, fingerprint));
    }

    /// <summary>Everything the review is written from, for this week or one of the history's weeks.</summary>
    internal sealed record WeekFacts(WeekReport Week, bool WeekOver, int DayOfWeek, int DrillAttempts, int DrillCorrect, int DrillGoal, PerformanceFigures Results);

    private async Task<WeekFacts?> MeasureAsync(Guid userId, DateOnly? week, CancellationToken cancellationToken)
    {
        var report = await progress.GetAsync(userId, cancellationToken);
        var current = week is null || week == report.WeekStart;
        var measured = current ? report.Current : report.History.FirstOrDefault(h => h.Plan.WeekStart == week);
        if (measured is null)
        {
            return null;
        }

        var start = WeeklyPlanner.Start(measured.Plan.WeekStart);
        var end = start.AddDays(7);
        var (attempts, correct) = current
            ? (report.DrillAttempts, report.DrillCorrect)
            : await training.CountSinceAsync(userId, start, end, cancellationToken);
        var results = (await performance.GetAsync(userId, start, end, cancellationToken)).Totals;
        var now = time.GetUtcNow();
        return new WeekFacts(measured, now >= end, Math.Clamp((int)(now - start).TotalDays + 1, 1, 7), attempts, correct, report.DrillGoal, results);
    }

    /// <summary>The week in plain facts, all computed by us.</summary>
    internal static string Facts(WeekFacts facts, IReadOnlyList<ReviewedPriority> priorities)
    {
        string Pct(decimal ratio) => (ratio * 100m).ToString("0.#", CultureInfo.InvariantCulture) + " %";
        var text = new StringBuilder();
        var r = facts.Results;
        text.AppendLine(facts.WeekOver
            ? "The week is over: review it as a whole."
            : string.Create(CultureInfo.InvariantCulture, $"The week is in progress (day {facts.DayOfWeek} of 7): this is a mid-week check, not a verdict."));
        text.AppendLine(CultureInfo.InvariantCulture, $"Volume: {facts.Week.Hands} hands at {(facts.Week.Plan.Format == TableFormat.SixMax ? "6-max" : "full-ring")} tables (15+ BB), {r.Tournaments} tournaments.");
        if (r.TournamentsWithResult > 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Results ({r.TournamentsWithResult} tournaments with a known result): profit {r.Profit.ToString("0.00", CultureInfo.InvariantCulture)}, ROI {(r.Roi is { } roi ? Pct(roi) : "unknown")}, {r.PaidEntries} paid of {r.Entries} entries. A week of tournaments is mostly variance: never judge the player's level or the plan from the money.");
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"Training-room drills this week: {facts.DrillAttempts} answers, {facts.DrillCorrect} right; weekly goal {facts.DrillGoal}.");
        text.AppendLine();
        text.AppendLine("The week's priorities, chosen on Monday from the leaks of the previous 90 days, and how they moved on this week's hands:");
        foreach (var p in priorities)
        {
            var where = p.Position is { } position ? $" from {HandNarrator.Label(position)}" : string.Empty;
            text.Append(CultureInfo.InvariantCulture, $"- {p.Ref}: {p.Stat}{where}, {(p.Direction == LeakDirection.TooHigh ? "too high" : "too low")}. Baseline {Pct(p.BaselineRate)}, reference {Pct(p.Min)}–{Pct(p.Max)}. ");
            text.AppendLine(p.Rate is { } rate
                ? string.Create(CultureInfo.InvariantCulture, $"This week: {Pct(rate)} over {p.Opportunities} spots: {Status(p.Status)}.")
                : string.Create(CultureInfo.InvariantCulture, $"This week: {p.Opportunities} spots: {Status(p.Status)}."));
        }

        return text.ToString();
    }

    internal static string Fingerprint(WeekFacts facts, string language)
    {
        var key = string.Join(
            '|',
            Version.ToString(CultureInfo.InvariantCulture),
            facts.Week.Plan.WeekStart.ToString("O", CultureInfo.InvariantCulture),
            facts.Week.Plan.CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture),
            facts.Week.Hands.ToString(CultureInfo.InvariantCulture),
            string.Join(',', facts.Week.Priorities.Select(p => $"{p.Week.Made}/{p.Week.Opportunities}")),
            (facts.DrillAttempts / 10).ToString(CultureInfo.InvariantCulture),
            facts.Results.TournamentsWithResult.ToString(CultureInfo.InvariantCulture),
            facts.WeekOver.ToString(),
            language);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    private static string Status(PriorityStatus status) => status switch
    {
        PriorityStatus.InRange => "inside the reference range",
        PriorityStatus.Improving => "still outside the range, but closer than the baseline",
        PriorityStatus.OffTrack => "as far from the range as the baseline, or further",
        _ => "fewer than 10 spots, not judged",
    };

    private static ReportView<WeekReviewReport> View(StoredReport<WeekReviewReport> stored, string fingerprint) =>
        new(stored.Report, stored.Fingerprint != fingerprint, stored.Model, stored.CreatedAt);

    private static string Subject(DateOnly week) => week.ToString("O", CultureInfo.InvariantCulture);
}
