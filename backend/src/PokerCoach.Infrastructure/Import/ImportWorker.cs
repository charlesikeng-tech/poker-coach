using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PokerCoach.Application.Import;
using PokerCoach.Application.Notifications;
using PokerCoach.Application.Statistics;
using PokerCoach.Application.Tournaments;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Infrastructure.Persistence;
using PokerCoach.Infrastructure.Poker;
using PokerCoach.Infrastructure.Statistics;

namespace PokerCoach.Infrastructure.Import;

/// <summary>
/// Drains the import queue with configurable concurrency (each file claimed via SKIP LOCKED),
/// then brings per-hand statistics facts up to date (ADR-0006) and each tournament's hand coverage.
/// Polling keeps it simple; LISTEN/NOTIFY can cut the latency later if it ever matters.
/// </summary>
internal sealed partial class ImportWorker(
    IServiceScopeFactory scopes,
    ImportOptions options,
    TimeProvider time,
    ILogger<ImportWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(10);

    // Once facts are up to date, look again only after an import or this long (cheap, but not free).
    private static readonly TimeSpan FactsRecheckInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.WorkerEnabled)
        {
            LogDisabled(logger);
            return;
        }

        var concurrency = Math.Clamp(options.WorkerConcurrency, 1, 32);
        var factsDue = true;
        var factsCheckedAt = DateTimeOffset.MinValue;
        var affectedUserIds = new HashSet<Guid>();
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan? delay;
            try
            {
                var imported = await ProcessFilesAsync(concurrency, stoppingToken);
                var factsComputed = 0;
                if (imported)
                {
                    factsDue = true;
                }
                else if (factsDue || time.GetUtcNow() - factsCheckedAt > FactsRecheckInterval)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var (hands, userIds) = await scope.ServiceProvider.GetRequiredService<HandFactsBackfill>().ProcessBatchAsync(stoppingToken);
                    factsComputed = hands;
                    foreach (var uid in userIds)
                        affectedUserIds.Add(uid);

                    if (factsComputed == 0)
                    {
                        factsComputed = await scope.ServiceProvider.GetRequiredService<OpponentStatsBackfill>().ProcessBatchAsync(stoppingToken);
                    }

                    if (factsComputed == 0)
                    {
                        factsComputed = await scope.ServiceProvider.GetRequiredService<CoverageBackfill>()
                            .ProcessBatchAsync(HeroHandFacts.Version, stoppingToken);
                    }

                    factsCheckedAt = time.GetUtcNow();
                    factsDue = factsComputed > 0;

                    if (!factsDue && affectedUserIds.Count > 0)
                    {
                        _ = SyncContactsAsync(scopes, affectedUserIds, logger);
                        affectedUserIds = [];
                    }
                }

                delay = imported || factsComputed > 0 ? null : IdleDelay;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The worker must survive any failure: the file was released and will be retried.
                LogProcessingFailed(logger, exception);
                delay = ErrorDelay;
            }

            if (delay is { } wait)
            {
                try
                {
                    await Task.Delay(wait, time, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Processes up to <paramref name="concurrency"/> queued files in parallel (each in its own DI scope).
    /// Returns true when at least one file was processed.
    /// </summary>
    private async Task<bool> ProcessFilesAsync(int concurrency, CancellationToken stoppingToken)
    {
        if (concurrency == 1)
        {
            return await ProcessOneFileAsync(stoppingToken);
        }

        var tasks = new Task<bool>[concurrency];
        for (var i = 0; i < concurrency; i++)
        {
            tasks[i] = ProcessOneFileAsync(stoppingToken);
        }

        var results = await Task.WhenAll(tasks);
        return results.Any(static r => r);
    }

    private async Task<bool> ProcessOneFileAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ImportProcessor>().ProcessNextAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            // Per-file errors are logged and swallowed: the file was already released by ImportProcessor
            // and the queue should be rechecked rather than jumping straight to the facts pipeline.
            LogProcessingFailed(logger, exception);
            return true;
        }
    }

    private static async Task SyncContactsAsync(IServiceScopeFactory scopes, IReadOnlyCollection<Guid> userIds, ILogger logger)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var contactSync = services.GetRequiredService<IContactSync>();
            var db = services.GetRequiredService<PokerCoachDbContext>();

            foreach (var userId in userIds)
            {
                var user = await db.Set<User>().AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.Email })
                    .FirstOrDefaultAsync(CancellationToken.None);
                if (user?.Email is null)
                    continue;

                var stats = await (
                        from f in db.Set<HandHeroFactsRecord>().AsNoTracking()
                        join a in db.Set<PokerAccountRecord>().AsNoTracking() on f.PokerAccountId equals a.Id
                        where a.UserId == userId && f.FactsVersion == HeroHandFacts.Version
                        group f by 1 into g
                        select new { Total = g.Count(), MainGameType = g.GroupBy(x => x.GameType).OrderByDescending(x => x.Count()).Select(x => x.Key).FirstOrDefault() })
                    .FirstOrDefaultAsync(CancellationToken.None);

                var rooms = await db.Set<PokerAccountRecord>().AsNoTracking()
                    .Where(a => a.UserId == userId && a.ConfirmedAt != null)
                    .Select(a => a.Room)
                    .Distinct()
                    .ToListAsync(CancellationToken.None);

                await contactSync.SyncAsync(
                    new ContactAttributes(
                        user.Email,
                        TotalHands: stats?.Total ?? 0,
                        Rooms: rooms.Count > 0 ? string.Join(", ", rooms) : null,
                        MainGameType: stats?.MainGameType.ToString()),
                    CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            // Fire-and-forget: import processing must never fail because of Brevo.
            LogContactSyncFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Import worker disabled by configuration (Import:WorkerEnabled).")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Import processing failed; the file will be retried.")]
    private static partial void LogProcessingFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Contact sync after import failed (non-blocking).")]
    private static partial void LogContactSyncFailed(ILogger logger, Exception exception);
}
