using Microsoft.Extensions.Options;
using PokerCoach.Application.Identity;

namespace PokerCoach.Api.Hosting;

/// <summary>Configuration section "Cleanup".</summary>
public sealed class CleanupOptions
{
    public const string SectionName = "Cleanup";

    /// <summary>Unconfirmed accounts older than this are deleted. Default: 7 days.</summary>
    public int UnconfirmedAccountDays { get; set; } = 7;
}

/// <summary>
/// Deletes accounts created with an email and password that were never confirmed (ADR-0013).
/// Runs once at startup then every 24 hours. Safe to run concurrently: ExecuteDeleteAsync is atomic
/// and the condition on ConfirmedAt prevents deleting confirmed accounts even under a race.
/// </summary>
internal sealed class UnconfirmedAccountCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<CleanupOptions> options,
    ILogger<UnconfirmedAccountCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once immediately on startup, then every 24 hours.
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-options.Value.UnconfirmedAccountDays);
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IAccountCleanupStore>();
            var deleted = await store.DeleteExpiredUnconfirmedAsync(cutoff, cancellationToken);
            if (deleted > 0)
            {
                logger.LogInformation("Cleanup: deleted {Count} expired unconfirmed account(s).", deleted);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unconfirmed account cleanup failed.");
        }
    }
}
