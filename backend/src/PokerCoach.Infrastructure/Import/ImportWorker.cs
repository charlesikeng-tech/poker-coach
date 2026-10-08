using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PokerCoach.Application.Import;

namespace PokerCoach.Infrastructure.Import;

/// <summary>
/// Drains the import queue: one file at a time per instance, several instances share the queue safely
/// (claims use SKIP LOCKED). Polling keeps it simple; LISTEN/NOTIFY can cut the latency later if the
/// 2-second delay ever matters.
/// </summary>
internal sealed partial class ImportWorker(
    IServiceScopeFactory scopes,
    ImportOptions options,
    TimeProvider time,
    ILogger<ImportWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.WorkerEnabled)
        {
            LogDisabled(logger);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan? delay;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var processed = await scope.ServiceProvider.GetRequiredService<ImportProcessor>().ProcessNextAsync(stoppingToken);
                delay = processed ? null : IdleDelay;
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
