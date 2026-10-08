using PokerCoach.Domain.Poker;

namespace PokerCoach.Application.Poker;

/// <param name="ConfirmedAt">
/// Null until the user confirms the account is theirs. Accounts are created automatically from the hero
/// pseudonym found in imported files (ADR-0005); the confirmation guards against importing someone
/// else's files by mistake.
/// </param>
public sealed record PokerAccountView(Guid Id, PokerRoom Room, string ScreenName, DateTimeOffset CreatedAt, DateTimeOffset? ConfirmedAt);

public interface IPokerAccountStore
{
    Task<IReadOnlyList<PokerAccountView>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <returns>False when the account does not exist or belongs to another user.</returns>
    Task<bool> ConfirmAsync(Guid userId, Guid accountId, DateTimeOffset confirmedAt, CancellationToken cancellationToken);

    /// <summary>Deletes the account with its tournaments, hands and the files imported into it.</summary>
    /// <returns>False when the account does not exist or belongs to another user.</returns>
    Task<bool> DeleteAsync(Guid userId, Guid accountId, CancellationToken cancellationToken);
}

public sealed class PokerAccountService(IPokerAccountStore store, TimeProvider time)
{
    public Task<IReadOnlyList<PokerAccountView>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        store.ListAsync(userId, cancellationToken);

    /// <summary>Idempotent: confirming twice keeps the first confirmation date.</summary>
    public Task<bool> ConfirmAsync(Guid userId, Guid accountId, CancellationToken cancellationToken) =>
        store.ConfirmAsync(userId, accountId, time.GetUtcNow(), cancellationToken);

    /// <summary>"This is not me": removes everything imported under that pseudonym.</summary>
    public Task<bool> DeleteAsync(Guid userId, Guid accountId, CancellationToken cancellationToken) =>
        store.DeleteAsync(userId, accountId, cancellationToken);
}
