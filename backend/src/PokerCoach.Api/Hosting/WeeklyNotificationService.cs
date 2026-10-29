using Microsoft.Extensions.Options;
using PokerCoach.Api.Authentication;
using PokerCoach.Application.Notifications;

namespace PokerCoach.Api.Hosting;

/// <summary>
/// Sends the weekly plan reminder every Monday between 08:00 and 09:00 UTC.
/// Checks every hour; the notification store deduplicates so re-running is safe.
/// </summary>
internal sealed partial class WeeklyNotificationBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<AppOptions> app,
    ILogger<WeeklyNotificationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try
        {
            do
            {
                await TryRunAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down: expected, not an error.
        }
    }

    private async Task TryRunAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (now.DayOfWeek != DayOfWeek.Monday || now.Hour < 8 || now.Hour >= 9)
        {
            return;
        }

        var baseUrl = ResolveBaseUrl();
        if (baseUrl is null)
        {
            LogNoPublicUrl(logger);
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<WeeklyNotificationService>();
            var sent = await service.SendAsync(baseUrl, cancellationToken);
            if (sent > 0)
            {
                LogSent(logger, sent);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRunFailed(logger, ex);
        }
    }

    private Uri? ResolveBaseUrl()
    {
        var url = app.Value.PublicUrl;
        if (url is null)
        {
            return null;
        }

        var abs = url.AbsoluteUri;
        return abs.EndsWith('/') ? url : new Uri(abs + "/");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Weekly notification skipped: App:PublicUrl is not configured.")]
    private static partial void LogNoPublicUrl(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Weekly notification: {Count} email(s) sent.")]
    private static partial void LogSent(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Weekly notification run failed.")]
    private static partial void LogRunFailed(ILogger logger, Exception ex);
}
