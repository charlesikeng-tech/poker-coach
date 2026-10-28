using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Hands;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Api.Hands;

/// <summary>A seat. Opponents include their screen name and archetype when enough data exists.</summary>
/// <param name="Cards">Text form ("Ah", "Tc"); null when unknown (not the hero, not shown).</param>
/// <param name="Collected">Chips collected from the pot(s), uncalled bets included.</param>
/// <param name="OpponentName">The opponent's screen name; null for the hero.</param>
/// <param name="OpponentType">Archetype classification (nit, tag, lag…); null when fewer than 20 hands or for the hero.</param>
/// <param name="OpponentHands">Number of hands observed; null for the hero.</param>
public sealed record ReplaySeatResponse(
    int SeatNumber,
    PokerPosition? Position,
    bool IsHero,
    bool IsDealt,
    long Stack,
    IReadOnlyList<string>? Cards,
    long Collected,
    string? OpponentName,
    string? OpponentType,
    int? OpponentHands,
    Guid? OpponentId);

/// <param name="Amount">Chips this action adds to the pot (a raise's increment, not its total).</param>
public sealed record ReplayActionResponse(Street Street, int SeatNumber, ActionKind Kind, long Amount, bool IsAllIn);

/// <param name="Index">Position of the hand in its tournament, from 1, in play order.</param>
public sealed record HandReplayResponse(
    Guid HandId,
    Guid? TournamentId,
    string? TournamentName,
    DateTimeOffset StartedAt,
    int Level,
    long SmallBlind,
    long BigBlind,
    long? Ante,
    int MaxSeats,
    int ButtonSeat,
    IReadOnlyList<ReplaySeatResponse> Seats,
    IReadOnlyList<ReplayActionResponse> Actions,
    IReadOnlyList<string> Board,
    int Index,
    int HandCount,
    Guid? PreviousHandId,
    Guid? NextHandId);

public static class HandEndpoints
{
    public static IEndpointRouteBuilder MapHandEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/hands/{id:guid}", GetAsync).WithTags("Hands");
        return endpoints;
    }

    private static string? ToCamelCase(string? value) =>
        value is { Length: > 0 } ? char.ToLowerInvariant(value[0]) + value[1..] : null;

    /// <summary>One hand for the replayer. Someone else's hand answers like a missing one.</summary>
    private static async Task<Results<Ok<HandReplayResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetAsync(
        Guid id,
        HttpContext context,
        HandReplayService hands,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var replay = await hands.GetAsync(userId, id, cancellationToken);
        if (replay is null)
        {
            return ApiProblems.WithCode(StatusCodes.Status404NotFound, "HAND_NOT_FOUND", "No such hand.");
        }

        return TypedResults.Ok(new HandReplayResponse(
            replay.HandId,
            replay.TournamentId,
            replay.TournamentName,
            replay.StartedAt,
            replay.Level,
            replay.SmallBlind,
            replay.BigBlind,
            replay.Ante,
            replay.MaxSeats,
            replay.ButtonSeat,
            replay.Seats
                .Select(s => new ReplaySeatResponse(
                    s.SeatNumber, s.Position, s.IsHero, s.IsDealt, s.Stack,
                    s.Cards?.Select(c => c.ToString()).ToList(), s.Collected,
                    s.OpponentName, ToCamelCase(s.OpponentType?.ToString()), s.OpponentHands, s.OpponentId))
                .ToList(),
            replay.Actions.Select(a => new ReplayActionResponse(a.Street, a.SeatNumber, a.Kind, a.Amount, a.IsAllIn)).ToList(),
            replay.Board.Select(c => c.ToString()).ToList(),
            replay.Index,
            replay.HandCount,
            replay.PreviousHandId,
            replay.NextHandId));
    }
}
