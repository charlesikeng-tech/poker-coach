namespace PokerCoach.Application.Analytics;

/// <summary>
/// Which features each user opened, one row per user, feature and day (ADR-0011): enough to know what
/// testers use and whether they come back, without a third-party tracker or a cookie banner.
/// </summary>
public interface IFeatureUsageStore
{
    /// <summary>Idempotent: recording the same user, feature and day twice keeps one row.</summary>
    Task RecordAsync(Guid userId, DateOnly day, string feature, CancellationToken cancellationToken);

    /// <summary>Removes days before <paramref name="day"/>; returns the rows removed.</summary>
    Task<int> PurgeBeforeAsync(DateOnly day, CancellationToken cancellationToken);
}
