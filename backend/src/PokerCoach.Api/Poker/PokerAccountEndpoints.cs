using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Application.Poker;
using PokerCoach.Domain.Poker;

namespace PokerCoach.Api.Poker;

/// <param name="ConfirmedAt">Null while the user has not confirmed the account detected at import is theirs.</param>
public sealed record PokerAccountResponse(Guid Id, PokerRoom Room, string ScreenName, DateTimeOffset CreatedAt, DateTimeOffset? ConfirmedAt);

public static class PokerAccountEndpoints
{
    public static IEndpointRouteBuilder MapPokerAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/poker-accounts").WithTags("Poker accounts");

        group.MapGet("/", ListAsync);
        group.MapPost("/{accountId:guid}/confirm", ConfirmAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapDelete("/{accountId:guid}", DeleteAsync).AddEndpointFilter<AntiforgeryValidationFilter>();

        return endpoints;
    }

    private static async Task<Results<Ok<IReadOnlyList<PokerAccountResponse>>, UnauthorizedHttpResult>> ListAsync(
        HttpContext context,
        PokerAccountService accounts,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var list = await accounts.ListAsync(userId, cancellationToken);
        return TypedResults.Ok<IReadOnlyList<PokerAccountResponse>>(
            list.Select(a => new PokerAccountResponse(a.Id, a.Room, a.ScreenName, a.CreatedAt, a.ConfirmedAt)).ToList());
    }

    private static async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> ConfirmAsync(
        Guid accountId,
        HttpContext context,
        PokerAccountService accounts,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        return await accounts.ConfirmAsync(userId, accountId, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    /// <summary>"Not my account": deletes it with everything imported under it.</summary>
    private static async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> DeleteAsync(
        Guid accountId,
        HttpContext context,
        PokerAccountService accounts,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        return await accounts.DeleteAsync(userId, accountId, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
