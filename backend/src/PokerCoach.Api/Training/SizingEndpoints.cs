using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Billing;
using PokerCoach.Api.Errors;
using PokerCoach.Api.Statistics;
using PokerCoach.Application.Subscriptions;
using PokerCoach.Application.Training;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Subscriptions;
using PokerCoach.Domain.Training;

namespace PokerCoach.Api.Training;

/// <summary>A size: <c>size</c> in big blinds for an open, a multiple of the open for a 3-bet; or all-in (size null).</summary>
public sealed record RaiseSizeDto(decimal? Size, bool AllIn)
{
    public static RaiseSizeDto From(RaiseSize size) => new(size.Value, size.IsAllIn);

    public RaiseSize ToDomain() => AllIn ? RaiseSize.AllIn : new RaiseSize(Size);
}

/// <param name="Opener">3-bet spots: the seat that opened (send it back with the answer); null for opens.</param>
/// <param name="OpenToBigBlinds">3-bet spots: the open faced, raise-to in big blinds.</param>
/// <param name="StackInBigBlinds">Every stack at the table, so also the effective stack.</param>
/// <param name="Hand">A hand the reference plays there ("AKs"): the decision to raise is given, the size is asked.</param>
/// <param name="Choices">Sizes to pick from.</param>
public sealed record SizingSpotResponse(
    TableFormat Format,
    StackBand Band,
    SizingKind Kind,
    PokerPosition Position,
    PokerPosition? Opener,
    decimal? OpenToBigBlinds,
    decimal StackInBigBlinds,
    string Hand,
    IReadOnlyList<string> Cards,
    IReadOnlyList<RaiseSizeDto> Choices,
    bool Review);

/// <summary>The spot as received, and the size chosen among its choices.</summary>
public sealed record SizingAnswerRequest(
    TableFormat? Format,
    StackBand? Band,
    PokerPosition? Position,
    decimal? StackInBigBlinds,
    PokerPosition? Opener,
    decimal? OpenToBigBlinds,
    RaiseSizeDto? Answer);

/// <param name="Verdict">The size answered: inLine, tooSmall or tooBig.</param>
/// <param name="Min">Smallest size still in line (same unit as the size); null for an all-in reference.</param>
/// <param name="BestRaiseToBigBlinds">The reference size as a raise-to in big blinds; null when all-in.</param>
/// <param name="InPosition">3-bets: whether the 3-bettor plays the rest of the hand in position.</param>
public sealed record SizingResultResponse(
    bool Correct,
    SizingVerdict Verdict,
    RaiseSizeDto Best,
    decimal? Min,
    decimal? Max,
    decimal? BestRaiseToBigBlinds,
    bool InPosition,
    int ReferenceVersion);

public sealed record SizingProgressResponse(int Attempts, int Correct, int Streak, int DueReviews);

/// <param name="Seat">Opens: the seat (by distance to the button). Null for 3-bets.</param>
/// <param name="InPosition">3-bets: in or out of position. Null for opens.</param>
/// <param name="TypicalSize">Median of the sizes that were not all-in (big blinds for opens, multiples for 3-bets).</param>
/// <param name="Reference">The reference size most of these hands called for.</param>
/// <param name="OffHands">Most recent hands off the reference, to replay.</param>
public sealed record PlayedSizingGroupResponse(
    SizingKind Kind,
    PokerPosition? Seat,
    bool? InPosition,
    StackBand Band,
    int Hands,
    int InLine,
    int TooSmall,
    int TooBig,
    int AllIns,
    decimal? TypicalSize,
    RaiseSizeDto Reference,
    IReadOnlyList<Guid> OffHands);

public sealed record PlayedSizingsResponse(TableFormat Format, IReadOnlyList<PlayedSizingGroupResponse> Groups, int PendingHands, int ReferenceVersion);

/// <summary>
/// Sizing (ADR-0020): drills on how much to open or 3-bet, checked against the reference sizes v1, and the player's
/// own sizes next to the same reference. Same offer as the other drills.
/// </summary>
public static class SizingEndpoints
{
    public static IEndpointRouteBuilder MapSizingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/training/sizing").WithTags("Training").RequiresFeature(Feature.TrainingDrills);
        group.MapGet("/spot", GetSpotAsync);
        group.MapPost("/answers", AnswerAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapGet("/progress", GetProgressAsync);
        group.MapGet("/played", GetPlayedAsync);
        return endpoints;
    }

    /// <param name="format">sixMax (default) or fullRing.</param>
    /// <param name="band">short, mid (default) or deep (push/fold has a single size, all-in).</param>
    /// <param name="kind">open, threeBet, or omitted for both.</param>
    private static async Task<Results<Ok<SizingSpotResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetSpotAsync(
        HttpContext context,
        SizingTrainingService sizing,
        CancellationToken cancellationToken,
        string format = "sixMax",
        string band = "mid",
        string? kind = null)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (!TableFormatQuery.TryParse(format, out var tableFormat) || tableFormat is not { } parsedFormat)
        {
            return ApiProblems.Validation("format", "Must be sixMax or fullRing.");
        }

        if (!TryParseEnum<StackBand>(band, out var stackBand) || stackBand == StackBand.Push)
        {
            return ApiProblems.Validation("band", "Must be one of: short, mid, deep.");
        }

        SizingKind? sizingKind = null;
        if (kind is not null)
        {
            if (!TryParseEnum<SizingKind>(kind, out var parsedKind))
            {
                return ApiProblems.Validation("kind", "Must be open or threeBet.");
            }

            sizingKind = parsedKind;
        }

        var drill = await sizing.NextAsync(userId, parsedFormat, stackBand, sizingKind, cancellationToken);
        var item = drill.Spot.Item;
        return TypedResults.Ok(new SizingSpotResponse(
            item.Format,
            item.Band,
            item.Kind,
            item.Position,
            item.Opener,
            item.OpenToBigBlinds,
            item.StackBigBlinds,
            drill.Spot.Hand.ToString(),
            [drill.Spot.First.ToString(), drill.Spot.Second.ToString()],
            drill.Choices.Select(RaiseSizeDto.From).ToList(),
            drill.Review));
    }

    private static async Task<Results<Ok<SizingResultResponse>, ProblemHttpResult, UnauthorizedHttpResult>> AnswerAsync(
        SizingAnswerRequest request,
        HttpContext context,
        SizingTrainingService sizing,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (request is not { Format: { } format, Band: { } band, Position: { } position, StackInBigBlinds: { } stack, Answer: { } answer })
        {
            return ApiProblems.Validation("request", "format, band, position, stackInBigBlinds and answer are required.");
        }

        // The spot comes back from the client: it only grades the player's own answer, but it must still be a spot
        // the drill could have dealt.
        var (min, max) = ReferenceOpeningRanges.Bounds(band);
        if (band == StackBand.Push || stack < min || (max is { } upper && stack >= upper) || stack > 500m)
        {
            return ApiProblems.Validation("stackInBigBlinds", "Must be inside the band (short, mid or deep).");
        }

        if (request.Opener is { } opener)
        {
            if (!SizingDrill.ThreeBetPairs(format).Contains((position, opener)))
            {
                return ApiProblems.Validation("opener", "Not a 3-bet spot of the drill at this format.");
            }

            if (request.OpenToBigBlinds is not (>= 2m and <= 5m))
            {
                return ApiProblems.Validation("openToBigBlinds", "Required for a 3-bet, between 2 and 5.");
            }
        }
        else if (!ReferenceOpeningRanges.Positions(format).Contains(position))
        {
            return ApiProblems.Validation("position", "This seat does not open at this format.");
        }

        var item = new SizingItem(format, band, position, stack, request.Opener, request.Opener is null ? null : request.OpenToBigBlinds);
        var size = answer.ToDomain();
        if (!SizingDrill.IsOffered(item, size))
        {
            return ApiProblems.Validation("answer", "Must be one of the sizes offered for the spot.");
        }

        var result = await sizing.AnswerAsync(userId, item, size, cancellationToken);
        return TypedResults.Ok(new SizingResultResponse(
            result.Correct,
            result.Verdict,
            RaiseSizeDto.From(result.Advice.Best),
            result.Advice.Min,
            result.Advice.Max,
            result.Advice.BestRaiseToBigBlinds,
            result.InPosition,
            result.ReferenceVersion));
    }

    private static async Task<Results<Ok<SizingProgressResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetProgressAsync(
        HttpContext context,
        SizingTrainingService sizing,
        CancellationToken cancellationToken,
        string format = "sixMax")
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (!TableFormatQuery.TryParse(format, out var tableFormat) || tableFormat is not { } parsedFormat)
        {
            return ApiProblems.Validation("format", "Must be sixMax or fullRing.");
        }

        var progress = await sizing.ProgressAsync(userId, parsedFormat, cancellationToken);
        return TypedResults.Ok(new SizingProgressResponse(progress.Attempts, progress.Correct, progress.Streak, progress.DueReviews));
    }

    /// <summary>The player's own opens and 3-bets against a single open, judged hand by hand against the reference.</summary>
    /// <param name="format">sixMax (default) or fullRing.</param>
    /// <param name="from">Start of the period; clamped to the offer's history.</param>
    private static async Task<Results<Ok<PlayedSizingsResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetPlayedAsync(
        HttpContext context,
        SizingTrainingService sizing,
        PlanAccess plans,
        CancellationToken cancellationToken,
        string format = "sixMax",
        DateTimeOffset? from = null)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (!TableFormatQuery.TryParse(format, out var tableFormat) || tableFormat is not { } parsedFormat)
        {
            return ApiProblems.Validation("format", "Must be sixMax or fullRing.");
        }

        from = await plans.ClampFromAsync(userId, from, cancellationToken);
        var played = await sizing.PlayedAsync(userId, parsedFormat, from, cancellationToken);
        return TypedResults.Ok(new PlayedSizingsResponse(
            played.Format,
            played.Groups.Select(g => new PlayedSizingGroupResponse(
                g.Kind,
                g.Seat,
                g.InPosition,
                g.Band,
                g.Hands,
                g.InLine,
                g.TooSmall,
                g.TooBig,
                g.AllIns,
                g.TypicalSize,
                RaiseSizeDto.From(g.Reference),
                g.OffHands)).ToList(),
            played.PendingHands,
            played.ReferenceVersion));
    }

    private static bool TryParseEnum<T>(string value, out T parsed)
        where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out parsed) && Enum.IsDefined(parsed) && !int.TryParse(value, out _);
}
