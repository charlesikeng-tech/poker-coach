using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Evaluation;

namespace PokerCoach.Api.Poker;

/// <param name="Cards">Two cards in text form: "Ah", "Kd", "Tc", "2s".</param>
public sealed record EquityPlayerRequest(IReadOnlyList<string> Cards);

/// <param name="Players">2–6 players, each with exactly two hole cards.</param>
/// <param name="Board">Board cards already dealt (0–5). Must not overlap with hole cards.</param>
public sealed record EquityRequest(
    IReadOnlyList<EquityPlayerRequest> Players,
    IReadOnlyList<string>? Board);

/// <param name="Range">Hand-class indices (0–168) selected for this player.</param>
public sealed record RangeEquityPlayerRequest(IReadOnlyList<int> Range);

/// <param name="Players">2–6 players, each with a range of hand-class indices.</param>
/// <param name="Board">Board cards already dealt (0–5) in text form.</param>
/// <param name="Iterations">Monte Carlo samples (default 20 000, max 50 000).</param>
public sealed record RangeEquityRequest(
    IReadOnlyList<RangeEquityPlayerRequest> Players,
    IReadOnlyList<string>? Board,
    int? Iterations);

/// <param name="Equity">Expected share of the pot (0–1), including splits.</param>
public sealed record EquityPlayerResult(double Equity);

public sealed record EquityResponse(IReadOnlyList<EquityPlayerResult> Players);

public static class EquityEndpoints
{
    public static IEndpointRouteBuilder MapEquityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/tools/equity", Calculate)
            .AllowAnonymous()
            .WithTags("Tools");

        app.MapPost("/api/tools/equity/range", CalculateRange)
            .AllowAnonymous()
            .WithTags("Tools");

        return app;
    }

    private static Results<Ok<EquityResponse>, ProblemHttpResult> Calculate(EquityRequest request)
    {
        var n = request.Players.Count;
        if (n < 2 || n > 6)
        {
            return TypedResults.Problem(
                title: "Invalid player count",
                detail: "Between 2 and 6 players are required.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        if (request.Players.Any(p => p.Cards.Count != 2))
        {
            return TypedResults.Problem(
                title: "Invalid hand",
                detail: "Each player must have exactly 2 cards.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var boardStrings = request.Board ?? [];
        if (boardStrings.Count > 5)
        {
            return TypedResults.Problem(
                title: "Invalid board",
                detail: "The board has at most 5 cards.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var holeCards = new List<(Card, Card)>(n);
        foreach (var player in request.Players)
        {
            if (!Card.TryParse(player.Cards[0], out var c1) || !Card.TryParse(player.Cards[1], out var c2))
            {
                return TypedResults.Problem(
                    title: "Invalid card",
                    detail: $"Cards must be in rank+suit format: 2–9, T, J, Q, K, A followed by c/d/h/s. Got: {string.Join(", ", player.Cards)}",
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            holeCards.Add((c1, c2));
        }

        var board = new List<Card>(5);
        foreach (var s in boardStrings)
        {
            if (!Card.TryParse(s, out var card))
            {
                return TypedResults.Problem(
                    title: "Invalid board card",
                    detail: $"Invalid card: {s}",
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            board.Add(card);
        }

        double[][] equity;
        try
        {
            equity = ShowdownEquity.Compute(holeCards, board, [Enumerable.Range(0, n).ToArray()]);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(
                title: "Invalid cards",
                detail: ex.Message,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var players = Enumerable.Range(0, n)
            .Select(i => new EquityPlayerResult(Math.Round(equity[0][i], 4)))
            .ToList();

        return TypedResults.Ok(new EquityResponse(players));
    }

    private static Results<Ok<EquityResponse>, ProblemHttpResult> CalculateRange(RangeEquityRequest request)
    {
        var n = request.Players.Count;
        if (n < 2 || n > 6)
        {
            return TypedResults.Problem(
                title: "Invalid player count",
                detail: "Between 2 and 6 players are required.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        if (request.Players.Any(p => p.Range.Count == 0))
        {
            return TypedResults.Problem(
                title: "Empty range",
                detail: "Every player must have at least one hand class selected.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        if (request.Players.Any(p => p.Range.Any(i => i < 0 || i > 168)))
        {
            return TypedResults.Problem(
                title: "Invalid hand class",
                detail: "Hand class indices must be between 0 and 168.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var boardStrings = request.Board ?? [];
        if (boardStrings.Count > 5)
        {
            return TypedResults.Problem(
                title: "Invalid board",
                detail: "The board has at most 5 cards.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var boardCards = new List<int>(5);
        foreach (var s in boardStrings)
        {
            if (!Card.TryParse(s, out var card))
            {
                return TypedResults.Problem(
                    title: "Invalid board card",
                    detail: $"Invalid card: {s}",
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            boardCards.Add(HandEvaluator.Index(card));
        }

        var iterations = Math.Clamp(request.Iterations ?? 20_000, 1_000, 50_000);
        var ranges = request.Players.Select(p => p.Range).ToList();

        var equity = RangeEquity.Compute(ranges, boardCards, iterations, Random.Shared);

        var players = Enumerable.Range(0, n)
            .Select(i => new EquityPlayerResult(Math.Round(equity[i], 4)))
            .ToList();

        return TypedResults.Ok(new EquityResponse(players));
    }
}
