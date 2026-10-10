using System.Collections.Concurrent;
using PokerCoach.Api.Authentication;
using PokerCoach.Application.Analytics;

namespace PokerCoach.Api.Analytics;

/// <summary>
/// Records which feature a signed-in user opened today (ADR-0011): the first path segment after
/// <c>/api/</c> ("statistics", "training"…). One database write per user, feature and day per instance;
/// later requests are answered from memory. Usage older than <see cref="RetentionDays"/> is purged when
/// the day changes.
/// </summary>
public sealed partial class FeatureUsageTracker(IServiceScopeFactory scopes, TimeProvider time, ILogger<FeatureUsageTracker> logger)
{
    /// <summary>About 13 months: a year-on-year comparison, no more.</summary>
    public const int RetentionDays = 400;

    private const int MaxFeatureLength = 32;

    private readonly Lock dayLock = new();
    private ConcurrentDictionary<(Guid UserId, string Feature), byte> seen = new();
    private DateOnly day;

    /// <summary>The feature a request path belongs to, or null when it is not a feature (auth, health…).</summary>
    public static string? FeatureOf(PathString path)
    {
        if (!path.StartsWithSegments("/api", out var rest) || !rest.HasValue)
        {
            return null;
        }

        var segment = rest.Value.AsSpan(1);
        var end = segment.IndexOf('/');
        var feature = (end < 0 ? segment : segment[..end]).ToString();
        return feature.Length is > 0 and <= MaxFeatureLength && feature != "auth" && feature.All(c => c is (>= 'a' and <= 'z') or '-')
            ? feature
            : null;
    }

    /// <summary>Never throws: losing a usage row must not fail the request it describes.</summary>
    public async Task RecordAsync(Guid userId, string feature)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var purge = false;
        ConcurrentDictionary<(Guid, string), byte> current;
        lock (dayLock)
        {
            if (today != day)
            {
                purge = day != default;
                day = today;
                seen = new();
            }

            current = seen;
        }

        if (!current.TryAdd((userId, feature), 0))
        {
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IFeatureUsageStore>();
            await store.RecordAsync(userId, today, feature, CancellationToken.None);
            if (purge)
            {
                var removed = await store.PurgeBeforeAsync(today.AddDays(-RetentionDays), CancellationToken.None);
                LogPurged(logger, removed);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Allow a retry on the next request.
            current.TryRemove((userId, feature), out _);
            LogFailed(logger, exception, feature);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Feature usage older than the retention period purged: {Rows} rows.")]
    private static partial void LogPurged(ILogger logger, int rows);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Feature usage could not be recorded for {Feature}.")]
    private static partial void LogFailed(ILogger logger, Exception exception, string feature);
}

public static class FeatureUsageTrackingExtensions
{
    /// <summary>After authentication: records successful requests of signed-in users, once the response is sent.</summary>
    public static IApplicationBuilder UseFeatureUsageTracking(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            await next(context);

            if (context.Response.StatusCode < 400
                && context.User.TryGetUserId(out var userId)
                && FeatureUsageTracker.FeatureOf(context.Request.Path) is { } feature)
            {
                var tracker = context.RequestServices.GetRequiredService<FeatureUsageTracker>();
                context.Response.OnCompleted(() => tracker.RecordAsync(userId, feature));
            }
        });
}
