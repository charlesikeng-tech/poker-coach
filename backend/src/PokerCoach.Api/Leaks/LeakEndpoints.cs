using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Billing;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Subscriptions;
using PokerCoach.Api.Statistics;
using PokerCoach.Application.Coaching;
using PokerCoach.Domain.Poker;
using PokerCoach.Application.Identity;
using PokerCoach.Application.Leaks;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Subscriptions;

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
/// <param name="Format">Table format analysed; positions are named within it.</param>
/// <param name="HandsByFormat">Hands (15+ BB) per format.</param>
/// <param name="LockedLeaks">Postflop leaks found but not shown: the plan does not include them (ADR-0016).</param>
public sealed record LeakAnalysisResponse(
    IReadOnlyList<LeakResponse> Leaks,
    IReadOnlyList<UnderSampledResponse> UnderSampled,
    int Hands,
    int Tournaments,
    int CompleteTournaments,
    int PendingHands,
    int ReferenceVersion,
    TableFormat Format,
    FormatCountsResponse HandsByFormat,
    int LockedLeaks);

/// <param name="Format">The table format the leak was shown for, as returned by GET /api/leaks.</param>
/// <param name="From">The period the leak was shown for, as in GET /api/leaks.</param>
/// <param name="Language">fr, en or es; defaults to the account's language.</param>
public sealed record ExplainLeakRequest(
    LeakStat? Stat,
    PokerPosition? Position,
    LeakDirection? Direction,
    TableFormat? Format,
    DateTimeOffset? From,
    string? Language);

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
        PlanAccess plans,
        ILeakService leaks,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? format = null,
        string? gameType = null)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (from is not null && to is not null && from >= to)
        {
            return ApiProblems.Validation("to", "Must be after 'from'.");
        }

        if (!TableFormatQuery.TryParse(format, out var tableFormat))
        {
            return ApiProblems.Validation("format", "Must be sixMax or fullRing.");
        }

        if (!GameTypeQuery.TryParse(gameType, out var parsedGameType))
        {
            return ApiProblems.Validation("gameType", "Must be tournament or cashGame.");
        }

        // Free plan: history window (ADR-0014). Data is kept, only not shown.
        from = await plans.ClampFromAsync(userId, from, cancellationToken);
        var analysis = await leaks.GetAsync(userId, tableFormat, from, to, cancellationToken, parsedGameType);

        // Postflop leaks are Elite (ADR-0016): counted, not shown, so the page can say what Elite would add.
        var advanced = (await plans.GetPlanAsync(userId, cancellationToken)).Allows(Feature.AdvancedLeaks);
        var shown = analysis.Report.Leaks.Where(l => advanced || !l.Stat.IsPostflop()).ToList();
        return TypedResults.Ok(new LeakAnalysisResponse(
            shown.Select(l => new LeakResponse(
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
            analysis.Report.UnderSampled.Where(u => advanced || !u.Stat.IsPostflop()).Select(u => new UnderSampledResponse(u.Stat, u.Position, u.Opportunities, u.Needed)).ToList(),
            analysis.Hands,
            analysis.Sample.Tournaments,
            analysis.Sample.CompleteTournaments,
            analysis.PendingHands,
            analysis.ReferenceVersion,
            analysis.Format,
            new FormatCountsResponse(analysis.HandsByFormat.SixMax, analysis.HandsByFormat.FullRing),
            analysis.Report.Leaks.Count - shown.Count));
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
        PlanAccess plans,
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

        if (stat.IsPostflop() && !(await plans.GetPlanAsync(userId, cancellationToken)).Allows(Feature.AdvancedLeaks))
        {
            return PlanGates.Required(Feature.AdvancedLeaks);
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
        // The leak is the one the page shows: same history window (ADR-0014).
        var from = await plans.ClampFromAsync(userId, request.From, cancellationToken);
        var outcome = await coach.ExplainAsync(userId, stat, request.Position, direction, request.Format, from, answerLanguage, cancellationToken);
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

        return Coaching.CoachingEndpoints.Problem(outcome.Failure!.Value, "LEAK_NOT_FOUND");
    }
}
