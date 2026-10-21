using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Api.Statistics;
using PokerCoach.Application.Coaching;
using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Progress;
using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Api.Coaching;

/// <param name="Language">fr, en or es; defaults to the account's language.</param>
public sealed record GenerateDebriefRequest(string? Language);

/// <param name="Day">The UTC day of the cash session ("2026-10-14"); required.</param>
public sealed record GenerateCashDebriefRequest(DateOnly Day, string? Language);

/// <param name="Week">Monday of the week to review ("2026-10-05"); this week when omitted.</param>
/// <param name="GameType">tournament or cashGame; defaults to tournament.</param>
/// <param name="SubFormat">mtt, rebuyMtt or sng; null for the combined tournament plan.</param>
public sealed record GenerateWeekReviewRequest(DateOnly? Week, string? Language, string? GameType, string? SubFormat = null);

/// <param name="Month">First day of the month ("2026-09-01"); previous month when omitted.</param>
public sealed record GenerateMonthlyReportRequest(DateOnly? Month, string? Language);

/// <summary>A key moment: our figures; <c>verdict</c> and <c>note</c> are the coach's, null when not commented.</summary>
public sealed record DebriefMomentResponse(
    string Ref,
    Guid HandId,
    int Level,
    PokerPosition? Position,
    string? HeroCards,
    decimal StackInBigBlinds,
    decimal NetBigBlinds,
    decimal StackShare,
    KeyMomentStage Stage,
    decimal? AllInEquity,
    MomentVerdict? Verdict,
    string? Note);

/// <summary>AI-written text: label it as such. <c>stale</c>: the tournament's facts changed since it was written.</summary>
public sealed record DebriefResponse(
    string Headline,
    string Story,
    IReadOnlyList<DebriefMomentResponse> Moments,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> WorkOn,
    bool Stale,
    string Model,
    DateTimeOffset CreatedAt);

/// <summary>A key moment in a cash session debrief.</summary>
public sealed record CashDebriefMomentResponse(
    string Ref,
    Guid HandId,
    long BigBlind,
    PokerPosition? Position,
    string? HeroCards,
    decimal StackInBigBlinds,
    decimal NetBigBlinds,
    MomentVerdict? Verdict,
    string? Note);

/// <summary>AI-written cash session debrief.</summary>
public sealed record CashDebriefResponse(
    DateOnly Day,
    string Headline,
    string Story,
    IReadOnlyList<CashDebriefMomentResponse> Moments,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> WorkOn,
    bool Stale,
    string Model,
    DateTimeOffset CreatedAt);

/// <summary>A priority as measured; <c>note</c> is the coach's comment, null when none.</summary>
public sealed record ReviewedPriorityResponse(
    string Ref,
    LeakStat Stat,
    PokerPosition? Position,
    LeakDirection Direction,
    PriorityStatus Status,
    decimal? Rate,
    int Opportunities,
    decimal BaselineRate,
    decimal Min,
    decimal Max,
    string? Note);

/// <summary>AI-written text: label it as such. <c>weekOver</c> false: a mid-week check.</summary>
public sealed record WeekReviewResponse(
    DateOnly WeekStart,
    bool WeekOver,
    string Headline,
    string Summary,
    IReadOnlyList<ReviewedPriorityResponse> Priorities,
    IReadOnlyList<string> NextSteps,
    bool Stale,
    string Model,
    DateTimeOffset CreatedAt);

/// <summary>AI-written monthly report.</summary>
public sealed record MonthlyReportResponse(
    DateOnly Month,
    string Headline,
    string Summary,
    IReadOnlyList<string> Progress,
    IReadOnlyList<string> WeakPoints,
    IReadOnlyList<string> NextMonth,
    int Hands,
    int Tournaments,
    decimal Profit,
    decimal? Roi,
    int CashHands,
    decimal? CashBbPer100,
    int LeakCount,
    int DrillAttempts,
    int DrillCorrect,
    bool Stale,
    string Model,
    DateTimeOffset CreatedAt);

/// <summary>
/// The coach's reports (ADR-0012): tournament debrief and weekly review. GET reads the stored report for
/// free; POST writes it (a paid model call) unless the stored one is still up to date.
/// </summary>
public static class CoachingEndpoints
{
    public static IEndpointRouteBuilder MapCoachingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tournaments/{id:guid}/debrief", GetDebriefAsync).WithTags("Coaching");
        endpoints.MapPost("/api/tournaments/{id:guid}/debrief", GenerateDebriefAsync).WithTags("Coaching")
            .AddEndpointFilter<AntiforgeryValidationFilter>();
        endpoints.MapGet("/api/cash/debrief", GetCashDebriefAsync).WithTags("Coaching");
        endpoints.MapPost("/api/cash/debrief", GenerateCashDebriefAsync).WithTags("Coaching")
            .AddEndpointFilter<AntiforgeryValidationFilter>();
        endpoints.MapGet("/api/progress/review", GetReviewAsync).WithTags("Coaching");
        endpoints.MapPost("/api/progress/review", GenerateReviewAsync).WithTags("Coaching")
            .AddEndpointFilter<AntiforgeryValidationFilter>();
        endpoints.MapGet("/api/progress/monthly-report", GetMonthlyReportAsync).WithTags("Coaching");
        endpoints.MapPost("/api/progress/monthly-report", GenerateMonthlyReportAsync).WithTags("Coaching")
            .AddEndpointFilter<AntiforgeryValidationFilter>();
        return endpoints;
    }

    /// <summary>Maps a refusal shared by every coaching feature; the subject-specific ones are mapped by the caller.</summary>
    internal static ProblemHttpResult Problem(CoachingFailure failure, string notFoundCode) => failure switch
    {
        CoachingFailure.NotFound => ApiProblems.WithCode(StatusCodes.Status404NotFound, notFoundCode, "Not found."),
        CoachingFailure.HandsPending => ApiProblems.WithCode(StatusCodes.Status409Conflict, "HANDS_PENDING", "Hands are still being analysed. Try again in a moment."),
        CoachingFailure.NotEnoughData => ApiProblems.WithCode(StatusCodes.Status422UnprocessableEntity, "NOT_ENOUGH_HANDS", "Too few hands for the coach to comment on."),
        CoachingFailure.NotALeak => ApiProblems.WithCode(StatusCodes.Status404NotFound, "LEAK_NOT_FOUND", "This statistic is not a current leak."),
        CoachingFailure.PlanRequired => ApiProblems.WithCode(StatusCodes.Status403Forbidden, "PLAN_REQUIRED", "This feature is in the Pro plan."),
        CoachingFailure.PlanLimitReached => ApiProblems.WithCode(StatusCodes.Status429TooManyRequests, "PLAN_LIMIT_REACHED", "The Free plan's monthly allowance is used up."),
        CoachingFailure.DailyLimitReached => ApiProblems.WithCode(StatusCodes.Status429TooManyRequests, "COACHING_DAILY_LIMIT", "Daily limit of new coach texts reached."),
        CoachingFailure.BudgetExhausted => ApiProblems.WithCode(StatusCodes.Status503ServiceUnavailable, "COACHING_BUDGET_EXHAUSTED", "The coach is paused until next month."),
        CoachingFailure.Unavailable => ApiProblems.WithCode(StatusCodes.Status503ServiceUnavailable, "COACHING_UNAVAILABLE", "The coach is not configured."),
        _ => ApiProblems.WithCode(StatusCodes.Status502BadGateway, "COACHING_FAILED", "The coach could not answer. Try again."),
    };

    /// <summary>The requested language if supported, else the account's; a validation problem for an unknown one.</summary>
    internal static async Task<(string? Language, ProblemHttpResult? Problem)> LanguageAsync(
        string? requested,
        Guid userId,
        UserProfileService profiles,
        CancellationToken cancellationToken)
    {
        if (requested is not null && !UserLanguages.IsSupported(requested))
        {
            return (null, ApiProblems.Validation("language", $"Must be one of: {string.Join(", ", UserLanguages.All)}."));
        }

        return (requested ?? (await profiles.GetAsync(userId, cancellationToken))?.PreferredLanguage ?? UserLanguages.Default, null);
    }

    private static async Task<Results<Ok<DebriefResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetDebriefAsync(
        Guid id,
        string? language,
        HttpContext context,
        TournamentDebriefService debriefs,
        UserProfileService profiles,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var (answerLanguage, problem) = await LanguageAsync(language, userId, profiles, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var view = await debriefs.GetAsync(userId, id, answerLanguage!, cancellationToken);
        return view is null
            ? ApiProblems.WithCode(StatusCodes.Status404NotFound, "DEBRIEF_NOT_FOUND", "No debrief written for this tournament yet.")
            : TypedResults.Ok(ToResponse(view));
    }

    private static async Task<Results<Ok<DebriefResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GenerateDebriefAsync(
        Guid id,
        GenerateDebriefRequest? request,
        HttpContext context,
        TournamentDebriefService debriefs,
        UserProfileService profiles,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var (answerLanguage, problem) = await LanguageAsync(request?.Language, userId, profiles, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var outcome = await debriefs.GenerateAsync(userId, id, answerLanguage!, cancellationToken);
        return outcome.View is { } view ? TypedResults.Ok(ToResponse(view)) : Problem(outcome.Failure!.Value, "TOURNAMENT_NOT_FOUND");
    }

    private static async Task<Results<Ok<WeekReviewResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetReviewAsync(
        DateOnly? week,
        string? gameType,
        string? subFormat,
        string? language,
        HttpContext context,
        WeekReviewService reviews,
        UserProfileService profiles,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (!GameTypeQuery.TryParse(gameType, out var parsedGameType))
        {
            return ApiProblems.Validation("gameType", "Must be tournament or cashGame.");
        }

        if (!TournamentSubFormatQuery.TryParse(subFormat, out var parsedSubFormat))
        {
            return ApiProblems.Validation("subFormat", "Must be mtt, rebuyMtt or sng.");
        }

        var (answerLanguage, problem) = await LanguageAsync(language, userId, profiles, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var view = await reviews.GetAsync(userId, week, parsedGameType ?? GameType.Tournament, answerLanguage!, cancellationToken, parsedSubFormat);
        return view is null
            ? ApiProblems.WithCode(StatusCodes.Status404NotFound, "REVIEW_NOT_FOUND", "No review written for this week yet.")
            : TypedResults.Ok(ToResponse(view));
    }

    private static async Task<Results<Ok<WeekReviewResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GenerateReviewAsync(
        GenerateWeekReviewRequest? request,
        HttpContext context,
        WeekReviewService reviews,
        UserProfileService profiles,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (!GameTypeQuery.TryParse(request?.GameType, out var parsedGameType))
        {
            return ApiProblems.Validation("gameType", "Must be tournament or cashGame.");
        }

        if (!TournamentSubFormatQuery.TryParse(request?.SubFormat, out var parsedSubFormat))
        {
            return ApiProblems.Validation("subFormat", "Must be mtt, rebuyMtt or sng.");
        }

        var (answerLanguage, problem) = await LanguageAsync(request?.Language, userId, profiles, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var outcome = await reviews.GenerateAsync(userId, request?.Week, parsedGameType ?? GameType.Tournament, answerLanguage!, cancellationToken, parsedSubFormat);
        return outcome.View is { } view ? TypedResults.Ok(ToResponse(view)) : Problem(outcome.Failure!.Value, "WEEK_NOT_FOUND");
    }

    private static async Task<Results<Ok<CashDebriefResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetCashDebriefAsync(
        DateOnly day,
        string? language,
        HttpContext context,
        CashSessionDebriefService debriefs,
        UserProfileService profiles,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var (answerLanguage, problem) = await LanguageAsync(language, userId, profiles, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var view = await debriefs.GetAsync(userId, day, answerLanguage!, cancellationToken);
        return view is null
            ? ApiProblems.WithCode(StatusCodes.Status404NotFound, "CASH_DEBRIEF_NOT_FOUND", "No debrief written for this cash session yet.")
            : TypedResults.Ok(ToCashResponse(day, view));
    }

    private static async Task<Results<Ok<CashDebriefResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GenerateCashDebriefAsync(
        GenerateCashDebriefRequest request,
        HttpContext context,
        CashSessionDebriefService debriefs,
        UserProfileService profiles,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var (answerLanguage, problem) = await LanguageAsync(request.Language, userId, profiles, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var outcome = await debriefs.GenerateAsync(userId, request.Day, answerLanguage!, cancellationToken);
        return outcome.View is { } view ? TypedResults.Ok(ToCashResponse(request.Day, view)) : Problem(outcome.Failure!.Value, "CASH_SESSION_NOT_FOUND");
    }

    private static async Task<Results<Ok<MonthlyReportResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetMonthlyReportAsync(
        DateOnly? month,
        string? language,
        HttpContext context,
        MonthlyReportService monthly,
        UserProfileService profiles,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var (answerLanguage, problem) = await LanguageAsync(language, userId, profiles, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var view = await monthly.GetAsync(userId, month, answerLanguage!, cancellationToken);
        return view is null
            ? ApiProblems.WithCode(StatusCodes.Status404NotFound, "MONTHLY_REPORT_NOT_FOUND", "No monthly report written for this month yet.")
            : TypedResults.Ok(ToMonthlyResponse(view));
    }

    private static async Task<Results<Ok<MonthlyReportResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GenerateMonthlyReportAsync(
        GenerateMonthlyReportRequest? request,
        HttpContext context,
        MonthlyReportService monthly,
        UserProfileService profiles,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var (answerLanguage, problem) = await LanguageAsync(request?.Language, userId, profiles, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var outcome = await monthly.GenerateAsync(userId, request?.Month, answerLanguage!, cancellationToken);
        return outcome.View is { } view ? TypedResults.Ok(ToMonthlyResponse(view)) : Problem(outcome.Failure!.Value, "MONTH_NOT_FOUND");
    }

    private static MonthlyReportResponse ToMonthlyResponse(ReportView<MonthlyReportData> view)
    {
        var data = view.Report;
        var r = data.Report;
        var f = data.Figures;
        return new MonthlyReportResponse(
            data.Month,
            r.Headline,
            r.Summary,
            r.Progress,
            r.WeakPoints,
            r.NextMonth,
            f.Hands,
            f.Tournaments,
            f.Profit,
            f.Roi,
            f.CashHands,
            f.CashBbPer100,
            f.LeakCount,
            f.DrillAttempts,
            f.DrillCorrect,
            view.Stale,
            view.Model,
            view.CreatedAt);
    }

    private static CashDebriefResponse ToCashResponse(DateOnly day, ReportView<CashDebriefReport> view)
    {
        var notes = view.Report.Debrief.Moments.ToDictionary(n => n.Ref, StringComparer.Ordinal);
        var debrief = view.Report.Debrief;
        return new CashDebriefResponse(
            day,
            debrief.Headline,
            debrief.Story,
            view.Report.Moments
                .Select(m =>
                {
                    var note = notes.GetValueOrDefault(m.Ref);
                    return new CashDebriefMomentResponse(m.Ref, m.HandId, m.BigBlind, m.Position, m.HeroCards, m.StackInBigBlinds, m.NetBigBlinds, note?.Verdict, note?.Note);
                })
                .ToList(),
            debrief.Strengths,
            debrief.WorkOn,
            view.Stale,
            view.Model,
            view.CreatedAt);
    }

    private static DebriefResponse ToResponse(ReportView<DebriefReport> view)
    {
        var notes = view.Report.Debrief.Moments.ToDictionary(n => n.Ref, StringComparer.Ordinal);
        var debrief = view.Report.Debrief;
        return new DebriefResponse(
            debrief.Headline,
            debrief.Story,
            view.Report.Moments
                .Select(m =>
                {
                    var note = notes.GetValueOrDefault(m.Ref);
                    return new DebriefMomentResponse(m.Ref, m.HandId, m.Level, m.Position, m.HeroCards, m.StackInBigBlinds, m.NetBigBlinds, m.StackShare, m.Stage, m.AllInEquity, note?.Verdict, note?.Note);
                })
                .ToList(),
            debrief.Strengths,
            debrief.WorkOn,
            view.Stale,
            view.Model,
            view.CreatedAt);
    }

    private static WeekReviewResponse ToResponse(ReportView<WeekReviewReport> view)
    {
        var notes = view.Report.Review.Priorities.ToDictionary(n => n.Ref, n => n.Note, StringComparer.Ordinal);
        var review = view.Report.Review;
        return new WeekReviewResponse(
            view.Report.WeekStart,
            view.Report.WeekOver,
            review.Headline,
            review.Summary,
            view.Report.Priorities
                .Select(p => new ReviewedPriorityResponse(p.Ref, p.Stat, p.Position, p.Direction, p.Status, p.Rate, p.Opportunities, p.BaselineRate, p.Min, p.Max, notes.GetValueOrDefault(p.Ref)))
                .ToList(),
            review.NextSteps,
            view.Stale,
            view.Model,
            view.CreatedAt);
    }
}
