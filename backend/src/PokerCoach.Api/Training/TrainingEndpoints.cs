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
public sealed record DrillSpotResponse(
    TableFormat Format,
    StackBand Band,
    PokerPosition Position,
    string Hand,
    IReadOnlyList<string> Cards,
    decimal StackInBigBlinds,
    bool Review,
    bool Focus);

public sealed record DrillAnswerRequest(TableFormat? Format, StackBand? Band, PokerPosition? Position, string? Hand, DrillAnswer? Answer);

/// <param name="ReferenceHands">The seat's reference range ("AA", "AKs"…), shown with the answer.</param>
public sealed record DrillResultResponse(
    DrillAnswer Expected,
    bool Correct,
    string ReferenceNotation,
    IReadOnlyList<string> ReferenceHands,
    int ReferenceVersion);

public sealed record SeatProgressResponse(PokerPosition Position, int Attempts, int Correct);

/// <param name="Streak">Correct answers in a row, latest first.</param>
/// <param name="DueReviews">Missed hands not yet answered right since.</param>
public sealed record DrillProgressResponse(int Attempts, int Correct, int Streak, int DueReviews, IReadOnlyList<SeatProgressResponse> BySeat);

/// <summary>The open-or-fold trainer (ADR-0009, block 3). The answer key is the reference ranges v1.</summary>
public static class TrainingEndpoints
{
    public static IEndpointRouteBuilder MapTrainingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/training/opening").WithTags("Training");
        group.MapGet("/spot", GetSpotAsync);
        group.MapPost("/answers", AnswerAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapGet("/progress", GetProgressAsync);
        return endpoints;
    }

    /// <param name="format">sixMax (default) or fullRing.</param>
    /// <param name="band">short, mid (default) or deep.</param>
    /// <param name="positions">Seats to train, comma-separated ("button,cutoff"); omitted: all.</param>
    private static async Task<Results<Ok<DrillSpotResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetSpotAsync(
        HttpContext context,
        OpeningTrainingService training,
        CancellationToken cancellationToken,
        string format = "sixMax",
        string band = "mid",
        string? positions = null)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (!TableFormatQuery.TryParse(format, out var tableFormat) || tableFormat is not { } parsedFormat)
        {
            return ApiProblems.Validation("format", "Must be sixMax or fullRing.");
        }

        if (!TryParseBand(band, out var stackBand))
        {
            return ApiProblems.Validation("band", "Must be one of: short, mid, deep.");
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

        var drill = await training.NextAsync(userId, parsedFormat, stackBand, seats, cancellationToken);
        var spot = drill.Spot;
        return TypedResults.Ok(new DrillSpotResponse(
            spot.Item.Format,
            spot.Item.Band,
            spot.Item.Position,
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

        if (!ReferenceOpeningRanges.Positions(format).Contains(position))
        {
            return ApiProblems.Validation("position", "This seat does not open at this format.");
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

        var result = await training.AnswerAsync(userId, new DrillItem(format, band, position, hands.Single()), answer, cancellationToken);
        return TypedResults.Ok(new DrillResultResponse(
            result.Expected,
            result.Correct,
            result.ReferenceNotation,
            result.ReferenceHands.Select(h => h.ToString()).ToList(),
            result.ReferenceVersion));
    }

    /// <param name="format">sixMax (default) or fullRing.</param>
    private static async Task<Results<Ok<DrillProgressResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetProgressAsync(
        HttpContext context,
        OpeningTrainingService training,
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

        var progress = await training.ProgressAsync(userId, parsedFormat, cancellationToken);
        return TypedResults.Ok(new DrillProgressResponse(
            progress.Attempts,
            progress.Correct,
            progress.Streak,
            progress.DueReviews,
            progress.BySeat.Select(s => new SeatProgressResponse(s.Position, s.Attempts, s.Correct)).ToList()));
    }

    private static bool TryParseBand(string value, out StackBand band) =>
        Enum.TryParse(value, ignoreCase: true, out band) && Enum.IsDefined(band) && !int.TryParse(value, out _);
}
