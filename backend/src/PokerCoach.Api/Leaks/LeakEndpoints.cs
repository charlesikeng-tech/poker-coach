using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Coaching;
using PokerCoach.Application.Identity;
using PokerCoach.Application.Leaks;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;

namespace PokerCoach.Api.Leaks;

/// <param name="Min">Lower bound of the reference range (ratio).</param>
public sealed record ReferenceRangeResponse(decimal Min, decimal Max);

/// <param name="Rate">The hero's rate (ratio); IntervalLow/High bound it at 95 %.</param>
public sealed record LeakResponse(
    LeakStat Stat,
    PokerPosition? Position,
    LeakDirection Direction,
    LeakConfidence Confidence,
    decimal Rate,
    int Made,
    int Opportunities,
    decimal IntervalLow,
    decimal IntervalHigh,
    ReferenceRangeResponse Range);

public sealed record UnderSampledResponse(LeakStat Stat, PokerPosition? Position, int Opportunities, int Needed);

/// <param name="Hands">Hands looked at: only those with 15+ big blinds (push/fold play is judged differently).</param>
/// <param name="PendingHands">Hands still being analyzed.</param>
/// <param name="ReferenceVersion">Version of the reference ranges used.</param>
public sealed record LeakAnalysisResponse(
    IReadOnlyList<LeakResponse> Leaks,
    IReadOnlyList<UnderSampledResponse> UnderSampled,
    int Hands,
    int Tournaments,
    int CompleteTournaments,
    int PendingHands,
    int ReferenceVersion);

/// <param name="From">The period the leak was shown for, as in GET /api/leaks.</param>
/// <param name="Language">fr, en or es; defaults to the account's language.</param>
public sealed record ExplainLeakRequest(LeakStat? Stat, PokerPosition? Position, LeakDirection? Direction, DateTimeOffset? From, string? Language);

/// <summary>An example hand as Poker Coach knows it (facts), next to the coach's note about it.</summary>
public sealed record ExampleHandResponse(string Ref, Guid HandId, DateTimeOffset StartedAt, int Level, PokerPosition? Position, string? HeroCards, decimal StackInBigBlinds, string Note);

/// <summary>AI-written text: label it as such in the UI. Figures shown next to it come from the analysis.</summary>
public sealed record LeakExplanationResponse(
    string Summary,
    string WhyItCosts,
    IReadOnlyList<string> Actions,
    IReadOnlyList<ExampleHandResponse> Hands,
    string Model,
    DateTimeOffset CreatedAt);

public static class LeakEndpoints
{
    public static IEndpointRouteBuilder MapLeakEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/leaks", GetAsync).WithTags("Leaks");
        endpoints.MapPost("/api/leaks/explanations", ExplainAsync).WithTags("Leaks").AddEndpointFilter<AntiforgeryValidationFilter>();
        return endpoints;
    }

    private static async Task<Results<Ok<LeakAnalysisResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetAsync(
        HttpContext context,
        LeakService leaks,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (from is not null && to is not null && from >= to)
        {
            return ApiProblems.Validation("to", "Must be after 'from'.");
        }

        var analysis = await leaks.GetAsync(userId, from, to, cancellationToken);
        return TypedResults.Ok(new LeakAnalysisResponse(
            analysis.Report.Leaks.Select(l => new LeakResponse(
                l.Stat,
                l.Position,
                l.Direction,
                l.Confidence,
                l.Rate,
                l.Made,
                l.Opportunities,
                l.IntervalLow,
                l.IntervalHigh,
                new ReferenceRangeResponse(l.Range.Min, l.Range.Max))).ToList(),
            analysis.Report.UnderSampled.Select(u => new UnderSampledResponse(u.Stat, u.Position, u.Opportunities, u.Needed)).ToList(),
            analysis.Hands,
            analysis.Sample.Tournaments,
            analysis.Sample.CompleteTournaments,
            analysis.PendingHands,
            analysis.ReferenceVersion));
    }

    /// <summary>
    /// Explains one current leak with the player's own hands (ADR-0008). Returns the stored explanation
    /// when the leak has not moved since; otherwise asks the model, within the daily and monthly limits.
    /// </summary>
    private static async Task<Results<Ok<LeakExplanationResponse>, ProblemHttpResult, UnauthorizedHttpResult>> ExplainAsync(
        ExplainLeakRequest request,
        HttpContext context,
        LeakCoachService coach,
        UserProfileService profiles,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (request.Stat is not { } stat)
        {
            return ApiProblems.Validation("stat", "Required.");
        }

        if (request.Direction is not { } direction)
        {
            return ApiProblems.Validation("direction", "Required.");
        }

        if (request.Language is not null && !UserLanguages.IsSupported(request.Language))
        {
            return ApiProblems.Validation("language", $"Must be one of: {string.Join(", ", UserLanguages.All)}.");
        }

        var answerLanguage = request.Language
            ?? (await profiles.GetAsync(userId, cancellationToken))?.PreferredLanguage
            ?? UserLanguages.Default;
        var outcome = await coach.ExplainAsync(userId, stat, request.Position, direction, request.From, answerLanguage, cancellationToken);
        if (outcome.Explanation is { } explanation)
        {
            var notes = explanation.Explanation.Hands.ToDictionary(h => h.Ref, h => h.Note, StringComparer.Ordinal);
            return TypedResults.Ok(new LeakExplanationResponse(
                explanation.Explanation.Summary,
                explanation.Explanation.WhyItCosts,
                explanation.Explanation.Actions,
                explanation.Hands
                    .Where(h => notes.ContainsKey(h.Ref))
                    .Select(h => new ExampleHandResponse(h.Ref, h.HandId, h.StartedAt, h.Level, h.Position, h.HeroCards, h.StackInBigBlinds, notes[h.Ref]))
                    .ToList(),
                explanation.Model,
                explanation.CreatedAt));
        }

        return outcome.Failure switch
        {
            CoachingFailure.NotALeak => ApiProblems.WithCode(StatusCodes.Status404NotFound, "LEAK_NOT_FOUND", "This statistic is not a current leak."),
            CoachingFailure.DailyLimitReached => ApiProblems.WithCode(StatusCodes.Status429TooManyRequests, "COACHING_DAILY_LIMIT", "Daily limit of new explanations reached."),
            CoachingFailure.BudgetExhausted => ApiProblems.WithCode(StatusCodes.Status503ServiceUnavailable, "COACHING_BUDGET_EXHAUSTED", "The coach is paused until next month."),
            CoachingFailure.Unavailable => ApiProblems.WithCode(StatusCodes.Status503ServiceUnavailable, "COACHING_UNAVAILABLE", "The coach is not configured."),
            _ => ApiProblems.WithCode(StatusCodes.Status502BadGateway, "COACHING_FAILED", "The coach could not answer. Try again."),
        };
    }
}
