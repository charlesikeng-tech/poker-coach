using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Errors;
using PokerCoach.Domain.Poker;

namespace PokerCoach.Api.Poker;

/// <param name="Stacks">Chip count per player (2–10 players).</param>
/// <param name="Payouts">Prize per finishing position, index 0 = 1st place. Length ≤ player count.</param>
public sealed record IcmRequest(IReadOnlyList<decimal> Stacks, IReadOnlyList<decimal> Payouts);

/// <param name="ChipShare">Fraction of total chips held by this player.</param>
/// <param name="IcmEquity">Monetary equity in the same unit as the submitted payouts.</param>
public sealed record IcmPlayerResult(decimal ChipShare, decimal IcmEquity);

public sealed record IcmResponse(IReadOnlyList<IcmPlayerResult> Players);

public static class IcmEndpoints
{
    public static IEndpointRouteBuilder MapIcmEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/tools/icm", Calculate)
            .AllowAnonymous()
            .WithTags("Tools");
        return app;
    }

    private static Results<Ok<IcmResponse>, ProblemHttpResult> Calculate(IcmRequest request)
    {
        var n = request.Stacks.Count;
        if (n < 2 || n > IcmCalculator.MaxPlayers)
        {
            return TypedResults.Problem(
                title: "Invalid player count",
                detail: $"Between 2 and {IcmCalculator.MaxPlayers} players are required.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        if (request.Payouts.Count == 0 || request.Payouts.Count > n)
        {
            return TypedResults.Problem(
                title: "Invalid payout structure",
                detail: "Payouts must have between 1 and the number of players entries.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        if (request.Stacks.Any(s => s <= 0) || request.Payouts.Any(p => p < 0))
        {
            return TypedResults.Problem(
                title: "Invalid values",
                detail: "Stacks must be positive. Payouts must be non-negative.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var equities = IcmCalculator.Calculate(
            [.. request.Stacks],
            [.. request.Payouts]);

        var totalChips = request.Stacks.Sum();
        var players = request.Stacks
            .Select((stack, i) => new IcmPlayerResult(
                ChipShare: totalChips > 0 ? stack / totalChips : 0,
                IcmEquity: equities[i]))
            .ToList();

        return TypedResults.Ok(new IcmResponse(players));
    }
}
