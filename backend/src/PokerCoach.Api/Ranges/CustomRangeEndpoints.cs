using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Billing;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Ranges;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Subscriptions;

namespace PokerCoach.Api.Ranges;

public sealed record SaveCustomOpeningRangeRequest(TableFormat Format, StackBand Band, PokerPosition Position, int[] Hands);

public sealed record SaveCustomDefenceRangeRequest(
    TableFormat Format, StackBand Band, PokerPosition Defender, PokerPosition Opener,
    int[] CallHands, int[] ThreeBetHands);

public sealed record CustomOpeningRangeResponse(TableFormat Format, StackBand Band, PokerPosition Position, int[] Hands, decimal Share);

public sealed record CustomDefenceRangeResponse(
    TableFormat Format, StackBand Band, PokerPosition Defender, PokerPosition Opener,
    int[] CallHands, int[] ThreeBetHands, decimal CallShare, decimal ThreeBetShare);

public sealed record CustomOpeningSlotResponse(TableFormat Format, StackBand Band, PokerPosition Position);

public sealed record CustomDefenceSlotResponse(TableFormat Format, StackBand Band, PokerPosition Defender, PokerPosition Opener);

public static class CustomRangeEndpoints
{
    public static IEndpointRouteBuilder MapCustomRangeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/ranges/custom")
            .WithTags("Custom Ranges")
            .RequiresFeature(Feature.CustomRanges);

        group.MapGet("/opening", GetOpeningAsync);
        group.MapPut("/opening", SaveOpeningAsync);
        group.MapDelete("/opening", DeleteOpeningAsync);
        group.MapGet("/opening/list", ListOpeningsAsync);

        group.MapGet("/defence", GetDefenceAsync);
        group.MapPut("/defence", SaveDefenceAsync);
        group.MapDelete("/defence", DeleteDefenceAsync);
        group.MapGet("/defence/list", ListDefencesAsync);

        return endpoints;
    }

    private static async Task<Results<Ok<CustomOpeningRangeResponse>, NotFound, UnauthorizedHttpResult>> GetOpeningAsync(
        HttpContext context,
        CustomRangeService service,
        CancellationToken cancellationToken,
        TableFormat format = TableFormat.SixMax,
        StackBand band = StackBand.Mid,
        PokerPosition position = PokerPosition.Button)
    {
        if (!context.User.TryGetUserId(out var userId))
            return TypedResults.Unauthorized();

        var range = await service.GetOpeningAsync(userId, format, band, position, cancellationToken);
        if (range is null)
            return TypedResults.NotFound();

        var handSet = range.ToHandClassSet();
        return TypedResults.Ok(new CustomOpeningRangeResponse(
            range.Format, range.Band, range.Position,
            ToIndices(range.Hands),
            ReferenceOpeningRanges.ComboShare(handSet)));
    }

    private static async Task<Results<NoContent, ProblemHttpResult, UnauthorizedHttpResult>> SaveOpeningAsync(
        SaveCustomOpeningRangeRequest request,
        HttpContext context,
        CustomRangeService service,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
            return TypedResults.Unauthorized();

        if (!ValidateIndices(request.Hands, out var error))
            return error;

        var indices = request.Hands.ToHashSet();
        await service.SaveOpeningAsync(userId, request.Format, request.Band, request.Position, indices, time.GetUtcNow(), cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> DeleteOpeningAsync(
        HttpContext context,
        CustomRangeService service,
        CancellationToken cancellationToken,
        TableFormat format = TableFormat.SixMax,
        StackBand band = StackBand.Mid,
        PokerPosition position = PokerPosition.Button)
    {
        if (!context.User.TryGetUserId(out var userId))
            return TypedResults.Unauthorized();

        return await service.DeleteOpeningAsync(userId, format, band, position, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<IReadOnlyList<CustomOpeningSlotResponse>>, UnauthorizedHttpResult>> ListOpeningsAsync(
        HttpContext context,
        CustomRangeService service,
        CancellationToken cancellationToken,
        TableFormat format = TableFormat.SixMax,
        StackBand band = StackBand.Mid)
    {
        if (!context.User.TryGetUserId(out var userId))
            return TypedResults.Unauthorized();

        var ranges = await service.ListOpeningsAsync(userId, format, band, cancellationToken);
        var slots = ranges.Select(r => new CustomOpeningSlotResponse(r.Format, r.Band, r.Position)).ToList();
        return TypedResults.Ok<IReadOnlyList<CustomOpeningSlotResponse>>(slots);
    }

    private static async Task<Results<Ok<CustomDefenceRangeResponse>, NotFound, UnauthorizedHttpResult>> GetDefenceAsync(
        HttpContext context,
        CustomRangeService service,
        CancellationToken cancellationToken,
        TableFormat format = TableFormat.SixMax,
        StackBand band = StackBand.Mid,
        PokerPosition defender = PokerPosition.BigBlind,
        PokerPosition opener = PokerPosition.Button)
    {
        if (!context.User.TryGetUserId(out var userId))
            return TypedResults.Unauthorized();

        var range = await service.GetDefenceAsync(userId, format, band, defender, opener, cancellationToken);
        if (range is null)
            return TypedResults.NotFound();

        var reference = range.ToDefenceReference();
        return TypedResults.Ok(new CustomDefenceRangeResponse(
            range.Format, range.Band, range.Defender, range.Opener,
            ToIndices(range.CallHands), ToIndices(range.ThreeBetHands),
            ReferenceOpeningRanges.ComboShare(reference.Call),
            ReferenceOpeningRanges.ComboShare(reference.ThreeBet)));
    }

    private static async Task<Results<NoContent, ProblemHttpResult, UnauthorizedHttpResult>> SaveDefenceAsync(
        SaveCustomDefenceRangeRequest request,
        HttpContext context,
        CustomRangeService service,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
            return TypedResults.Unauthorized();

        if (!ValidateIndices(request.CallHands, out var callError))
            return callError;

        if (!ValidateIndices(request.ThreeBetHands, out var threeBetError))
            return threeBetError;

        var callSet = request.CallHands.ToHashSet();
        var threeBetSet = request.ThreeBetHands.ToHashSet();
        if (callSet.Overlaps(threeBetSet))
            return ApiProblems.Validation("threeBetHands", "A hand cannot be in both call and 3-bet sets.");

        await service.SaveDefenceAsync(userId, request.Format, request.Band, request.Defender, request.Opener, callSet, threeBetSet, time.GetUtcNow(), cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> DeleteDefenceAsync(
        HttpContext context,
        CustomRangeService service,
        CancellationToken cancellationToken,
        TableFormat format = TableFormat.SixMax,
        StackBand band = StackBand.Mid,
        PokerPosition defender = PokerPosition.BigBlind,
        PokerPosition opener = PokerPosition.Button)
    {
        if (!context.User.TryGetUserId(out var userId))
            return TypedResults.Unauthorized();

        return await service.DeleteDefenceAsync(userId, format, band, defender, opener, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<IReadOnlyList<CustomDefenceSlotResponse>>, UnauthorizedHttpResult>> ListDefencesAsync(
        HttpContext context,
        CustomRangeService service,
        CancellationToken cancellationToken,
        TableFormat format = TableFormat.SixMax,
        StackBand band = StackBand.Mid)
    {
        if (!context.User.TryGetUserId(out var userId))
            return TypedResults.Unauthorized();

        var ranges = await service.ListDefencesAsync(userId, format, band, cancellationToken);
        var slots = ranges.Select(r => new CustomDefenceSlotResponse(r.Format, r.Band, r.Defender, r.Opener)).ToList();
        return TypedResults.Ok<IReadOnlyList<CustomDefenceSlotResponse>>(slots);
    }

    private static bool ValidateIndices(int[] indices, out ProblemHttpResult error)
    {
        error = default!;
        foreach (var index in indices)
        {
            if (index is < 0 or > 168)
            {
                error = ApiProblems.Validation("hands", "Each index must be between 0 and 168.");
                return false;
            }
        }
        return true;
    }

    private static int[] ToIndices(byte[] bitset)
    {
        var indices = new List<int>();
        for (var i = 0; i < 169; i++)
        {
            if ((bitset[i >> 3] & (1 << (i & 7))) != 0)
                indices.Add(i);
        }
        return indices.ToArray();
    }
}
