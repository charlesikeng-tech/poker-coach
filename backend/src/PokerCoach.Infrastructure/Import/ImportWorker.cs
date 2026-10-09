using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PokerCoach.Application.Import;
using PokerCoach.Application.Statistics;

namespace PokerCoach.Infrastructure.Import;

/// <summary>
/// Drains the import queue: one file at a time per instance, several instances share the queue safely
/// (claims use SKIP LOCKED). When the queue is empty, brings per-hand statistics facts up to date
/// (ADR-0006). Polling keeps it simple; LISTEN/NOTIFY can cut the latency later if it ever matters.
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

        var factsDue = true;
        var factsCheckedAt = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan? delay;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var imported = await scope.ServiceProvider.GetRequiredService<ImportProcessor>().ProcessNextAsync(stoppingToken);
                var factsComputed = 0;
                if (imported)
                {
                    factsDue = true;
                }
                else if (factsDue || time.GetUtcNow() - factsCheckedAt > FactsRecheckInterval)
                {
                    factsComputed = await scope.ServiceProvider.GetRequiredService<HandFactsBackfill>().ProcessBatchAsync(stoppingToken);
                    factsCheckedAt = time.GetUtcNow();
                    factsDue = factsComputed > 0;
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Import worker disabled by configuration (Import:WorkerEnabled).")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Import processing failed; the file will be retried.")]
    private static partial void LogProcessingFailed(ILogger logger, Exception exception);
}
