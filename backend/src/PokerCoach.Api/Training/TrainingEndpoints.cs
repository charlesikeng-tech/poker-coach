using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Api.Statistics;
using PokerCoach.Application.Training;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Training;

namespace PokerCoach.Api.Training;

/// <param name="Hand">Starting hand: "AKs".</param>
/// <param name="Cards">The two hole cards shown ("Ah", "Ks").</param>
/// <param name="Review">A hand missed before, asked again.</param>
/// <param name="Focus">The seat is weighted up: an opening leak was detected there.</param>
/// <param name="PushStack">Push/fold drills: the stack the answer is computed for; send it back with the answer.</param>
/// <param name="Shover">Defence drills: the seat that shoved (send it back with the answer); null for opening drills.</param>
public sealed record DrillSpotResponse(
    TableFormat Format,
    StackBand Band,
    PokerPosition Position,
    int? PushStack,
    PokerPosition? Shover,
    string Hand,
    IReadOnlyList<string> Cards,
    decimal StackInBigBlinds,
    bool Review,
    bool Focus,
    RealHandResponse? Real = null);

/// <summary>Quiz on real hands: where the spot comes from and what the hero did that day.</summary>
/// <param name="Remaining">Missed real spots left to fix, this one included.</param>
public sealed record RealHandResponse(Guid HandId, DateTimeOffset PlayedAt, RealAction Actual, int Remaining);

/// <param name="Shover">Set for a defence drill (answer call or fold); null for an opening drill (raise or fold).</param>
/// <param name="SourceHandId">Quiz on real hands: the hand of the spot (from the spot's <c>real.handId</c>).</param>
public sealed record DrillAnswerRequest(TableFormat? Format, StackBand? Band, PokerPosition? Position, int? PushStack, string? Hand, DrillAnswer? Answer, PokerPosition? Shover = null, Guid? SourceHandId = null);

/// <param name="ReferenceNotation">Null for computed push/fold ranges.</param>
/// <param name="ReferenceHands">The seat's reference range ("AA", "AKs"…), shown with the answer.</param>
public sealed record DrillResultResponse(
    DrillAnswer Expected,
    bool Correct,
    string? ReferenceNotation,
    IReadOnlyList<string> ReferenceHands,
    int ReferenceVersion);

public sealed record SeatProgressResponse(PokerPosition Position, int Attempts, int Correct);

/// <param name="Streak">Correct answers in a row, latest first.</param>
/// <param name="DueReviews">Missed hands not yet answered right since.</param>
public sealed record DrillProgressResponse(int Attempts, int Correct, int Streak, int DueReviews, IReadOnlyList<SeatProgressResponse> BySeat);

/// <summary>
/// The trainer (ADR-0009, block 3): open or fold (answer key: the reference ranges v1), and big blind
/// defence against a shove (answer key: the push/fold equilibrium's call ranges).
/// </summary>
public static class TrainingEndpoints
{
    public const string NoRealSpotLeft = "NO_REAL_SPOT_LEFT";

    public static IEndpointRouteBuilder MapTrainingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/training/opening").WithTags("Training");
        group.MapGet("/spot", GetSpotAsync);
        group.MapPost("/answers", AnswerAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapGet("/progress", GetProgressAsync);
        return endpoints;
    }

    /// <param name="format">sixMax (default) or fullRing.</param>
    /// <param name="band">push (below 15 BB), short, mid (default) or deep. Ignored in defence (always push).</param>
    /// <param name="positions">Seats to train, comma-separated ("button,cutoff"); omitted: all. In defence: the shovers.</param>
    /// <param name="mode">open (default), defence, or real (spots from the player's own hands he got wrong;
    /// 404 NO_REAL_SPOT_LEFT when none is left).</param>
    private static async Task<Results<Ok<DrillSpotResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetSpotAsync(
        HttpContext context,
        OpeningTrainingService training,
        CancellationToken cancellationToken,
        string format = "sixMax",
        string band = "mid",
        string? positions = null,
        string mode = "open")
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (!TableFormatQuery.TryParse(format, out var tableFormat) || tableFormat is not { } parsedFormat)
        {
            return ApiProblems.Validation("format", "Must be sixMax or fullRing.");
        }

        if (string.Equals(mode, "real", StringComparison.OrdinalIgnoreCase))
        {
            var real = await training.NextRealAsync(userId, parsedFormat, cancellationToken);
            if (real is null)
            {
                return ApiProblems.WithCode(StatusCodes.Status404NotFound, NoRealSpotLeft, "No missed real spot left to train.");
            }

            var realSpot = real.Drill.Spot;
            return TypedResults.Ok(new DrillSpotResponse(
                realSpot.Item.Format,
                realSpot.Item.Band,
                realSpot.Item.Position,
                realSpot.Item.PushStack,
                realSpot.Item.Shover,
                realSpot.Item.Hand.ToString(),
                [realSpot.First.ToString(), realSpot.Second.ToString()],
                realSpot.StackBigBlinds,
                real.Drill.Review,
                real.Drill.Focus,
                new RealHandResponse(real.HandId, real.PlayedAt, real.Actual, real.Remaining)));
        }

        if (!TryParseMode(mode, out var drillMode))
        {
            return ApiProblems.Validation("mode", "Must be open, defence or real.");
        }

        if (!TryParseBand(band, out var stackBand))
        {
            return ApiProblems.Validation("band", "Must be one of: push, short, mid, deep.");
        }

        var seats = new List<PokerPosition>();
        foreach (var value in (positions ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<PokerPosition>(value, ignoreCase: true, out var seat) || !Enum.IsDefined(seat) || int.TryParse(value, out _))
            {
                return ApiProblems.Validation("positions", $"Unknown position '{value}'.");
            }

            seats.Add(seat);
        }

        var drill = drillMode == DrillMode.Defence
            ? await training.NextDefenceAsync(userId, parsedFormat, seats, cancellationToken)
            : await training.NextAsync(userId, parsedFormat, stackBand, seats, cancellationToken);
        var spot = drill.Spot;
        return TypedResults.Ok(new DrillSpotResponse(
            spot.Item.Format,
            spot.Item.Band,
            spot.Item.Position,
            spot.Item.PushStack,
            spot.Item.Shover,
            spot.Item.Hand.ToString(),
            [spot.First.ToString(), spot.Second.ToString()],
            spot.StackBigBlinds,
            drill.Review,
            drill.Focus));
    }

    private static async Task<Results<Ok<DrillResultResponse>, ProblemHttpResult, UnauthorizedHttpResult>> AnswerAsync(
        DrillAnswerRequest request,
        HttpContext context,
        OpeningTrainingService training,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (request.Format is not { } format || request.Band is not { } band || request.Position is not { } position || request.Answer is not { } answer)
        {
            return ApiProblems.Validation("request", "format, band, position and answer are required.");
        }

        if (request.Shover is { } shover)
        {
            if (band != StackBand.Push || position != DefenceDrill.Defender || !DefenceDrill.Shovers(format).Contains(shover))
            {
                return ApiProblems.Validation("shover", "Defence drills are push/fold, from the big blind, against a seat that can shove.");
            }

            if (answer is not (DrillAnswer.Call or DrillAnswer.Fold))
            {
                return ApiProblems.Validation("answer", "Must be call or fold against a shove.");
            }
        }
        else
        {
            if (!ReferenceOpeningRanges.Positions(format).Contains(position))
            {
                return ApiProblems.Validation("position", "This seat does not open at this format.");
            }

            if (answer is not (DrillAnswer.Raise or DrillAnswer.Fold))
            {
                return ApiProblems.Validation("answer", "Must be raise or fold when folded to.");
            }
        }

        int? pushStack = null;
        if (band == StackBand.Push)
        {
            if (request.PushStack is not ({ } stack and >= PushFoldNash.MinStack and <= PushFoldNash.MaxStack))
            {
                return ApiProblems.Validation("pushStack", $"Required for push/fold, between {PushFoldNash.MinStack} and {PushFoldNash.MaxStack}.");
            }

            pushStack = stack;
        }

        IReadOnlySet<HandClass> hands;
        try
        {
            hands = RangeNotation.Parse(request.Hand ?? string.Empty);
        }
        catch (FormatException)
        {
            return ApiProblems.Validation("hand", "Must be one starting hand, like AKs.");
        }

        if (hands.Count != 1)
        {
            return ApiProblems.Validation("hand", "Must be one starting hand, like AKs.");
        }

        var result = await training.AnswerAsync(userId, new DrillItem(format, band, position, hands.Single(), pushStack, request.Shover), answer, cancellationToken, request.SourceHandId);
        return TypedResults.Ok(new DrillResultResponse(
            result.Expected,
            result.Correct,
            result.ReferenceNotation,
            result.ReferenceHands.Select(h => h.ToString()).ToList(),
            result.ReferenceVersion));
    }

    /// <param name="format">sixMax (default) or fullRing.</param>
    /// <param name="mode">open (default) or defence: by seat is the hero's seat, or the shover's.</param>
    private static async Task<Results<Ok<DrillProgressResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetProgressAsync(
        HttpContext context,
        OpeningTrainingService training,
        CancellationToken cancellationToken,
        string format = "sixMax",
        string mode = "open")
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (!TableFormatQuery.TryParse(format, out var tableFormat) || tableFormat is not { } parsedFormat)
        {
            return ApiProblems.Validation("format", "Must be sixMax or fullRing.");
        }

        if (!TryParseMode(mode, out var drillMode))
        {
            return ApiProblems.Validation("mode", "Must be open or defence.");
        }

        var progress = await training.ProgressAsync(userId, parsedFormat, drillMode, cancellationToken);
        return TypedResults.Ok(new DrillProgressResponse(
            progress.Attempts,
            progress.Correct,
            progress.Streak,
            progress.DueReviews,
            progress.BySeat.Select(s => new SeatProgressResponse(s.Position, s.Attempts, s.Correct)).ToList()));
    }

    private static bool TryParseMode(string value, out DrillMode mode) =>
        Enum.TryParse(value, ignoreCase: true, out mode) && Enum.IsDefined(mode) && !int.TryParse(value, out _);

    private static bool TryParseBand(string value, out StackBand band) =>
        Enum.TryParse(value, ignoreCase: true, out band) && Enum.IsDefined(band) && !int.TryParse(value, out _);
}
