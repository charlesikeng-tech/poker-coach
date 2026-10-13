namespace PokerCoach.Application.Identity;

/// <summary>Persistence port for the unconfirmed account cleanup job.</summary>
public interface IAccountCleanupStore
{
    /// <summary>
    /// Deletes users created before <paramref name="createdBefore"/> whose only credential is an
    /// unconfirmed password (no external identity). Returns the number of accounts deleted.
    /// </summary>
    Task<int> DeleteExpiredUnconfirmedAsync(DateTimeOffset createdBefore, CancellationToken cancellationToken);
}
