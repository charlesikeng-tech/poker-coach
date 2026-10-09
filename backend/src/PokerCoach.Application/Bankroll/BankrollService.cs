using PokerCoach.Application.Tournaments;
using PokerCoach.Domain.Bankroll;
using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Application.Bankroll;

public interface IBankrollStore
{
    Task<BankrollSettings?> GetSettingsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Creates or replaces the user's settings.</summary>
    Task SaveSettingsAsync(Guid userId, BankrollSettings settings, CancellationToken cancellationToken);

    /// <summary>Most recent first.</summary>
    Task<IReadOnlyList<BankrollMovement>> ListMovementsAsync(Guid userId, CancellationToken cancellationToken);

    Task AddMovementAsync(Guid userId, BankrollMovement movement, CancellationToken cancellationToken);

    /// <returns>False when the movement does not exist or belongs to someone else.</returns>
    Task<bool> DeleteMovementAsync(Guid userId, Guid movementId, CancellationToken cancellationToken);
}

/// <summary>Where the player stands against his buy-in rule.</summary>
/// <param name="MaxAverageBuyIn">Highest average buy-in the rule allows with the current balance.</param>
/// <param name="RecentAverageBuyIn">Average cost of the last <see cref="BankrollService.RecentTournaments"/> tournaments; null without any.</param>
/// <param name="AboveLimitRecently">Tournaments of the last <see cref="BankrollService.RecentDays"/> days whose buy-in is above the limit.</param>
/// <param name="PlayedRecently">Tournaments played in that window.</param>
public sealed record BuyInLimit(decimal MaxAverageBuyIn, decimal? RecentAverageBuyIn, int AboveLimitRecently, int PlayedRecently);

/// <param name="TournamentProfit">Results of the tournaments since the start date.</param>
/// <param name="NetDeposits">Deposits minus withdrawals plus adjustments since the start date.</param>
/// <param name="Tournaments">Tournaments with a known result since the start date.</param>
/// <param name="UnknownResults">Tournaments since the start date left out because their summary is missing.</param>
/// <param name="Roi">Over the whole history: skill does not restart with the bankroll.</param>
/// <param name="Risk">Null below <see cref="RiskSimulation.MinTournaments"/> results or with nothing left.</param>
public sealed record BankrollOverview(
    BankrollSettings Settings,
    decimal Balance,
    decimal TournamentProfit,
    decimal NetDeposits,
    int Tournaments,
    int UnknownResults,
    BuyInLimit Limit,
    IReadOnlyList<BalancePoint> Curve,
    IReadOnlyList<BankrollMovement> Movements,
    RoiEstimate Roi,
    RiskOutlook? Risk);

public enum MovementOutcome
{
    Added,

    /// <summary>The bankroll has no settings yet: there is no balance to move money in or out of.</summary>
    NotConfigured,

    /// <summary>Before the start date: it would not count, so it is refused rather than silently ignored.</summary>
    BeforeStart,
}

/// <summary>
/// The bankroll: balance since the start date (tournament results + money moved), the buy-in limit of
/// the chosen rule, and how risky the road ahead is given the player's own results.
/// Only tournaments with a known result count: a missing summary is never read as a zero.
/// </summary>
public sealed class BankrollService(IBankrollStore store, ITournamentReadStore tournaments, TimeProvider time)
{
    public const int MaxNoteLength = 200;

    /// <summary>Tournaments behind the "recent average buy-in".</summary>
    public const int RecentTournaments = 50;

    public const int RecentDays = 30;

    /// <summary>Tournaments simulated ahead: a few months of volume for a regular player.</summary>
    public const int SimulatedTournaments = 500;

    public async Task<BankrollOverview?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var settings = await store.GetSettingsAsync(userId, cancellationToken);
        if (settings is null)
        {
            return null;
        }

        var movements = await store.ListMovementsAsync(userId, cancellationToken);
        var items = (await tournaments.ListAsync(userId, null, null, cancellationToken))
            .Select(TournamentListService.ToItem)
            .Where(i => i.StartedAt is not null)
            .ToList();

        var known = items
            .Where(i => i.Result is { Status: TournamentResultStatus.Known, TotalBuyIn: not null, Profit: not null })
            .OrderBy(i => i.StartedAt)
            .ToList();
        var history = known.Select(ToMoney).ToList();
        var sinceStart = history.Where(t => t.PlayedAt >= settings.StartedOn).ToList();
        var countedMovements = movements.Where(m => m.OccurredAt >= settings.StartedOn).ToList();

        var curve = BankrollLedger.Curve(settings, sinceStart, countedMovements);
        var balance = curve[^1].Balance;
        var maxBuyIn = settings.MaxAverageBuyIn(balance);

        var recent = history.TakeLast(RecentTournaments).ToList();
        decimal? recentAverage = recent.Count == 0 ? null : Math.Round(recent.Average(t => t.Cost), 2);

        // Per-entry price, not the total cost: a re-entry does not make the tournament a bigger one.
        var windowStart = time.GetUtcNow().AddDays(-RecentDays);
        var window = items.Where(i => i.StartedAt >= windowStart).ToList();
        var aboveLimit = window.Count(i => i.BuyIn is { } b && b > maxBuyIn);

        var unknown = items.Count(i => i.StartedAt >= settings.StartedOn && i.Result.Status != TournamentResultStatus.Known);

        var risk = recentAverage is { } stake
            ? RiskSimulation.Simulate(new Random(Seed(userId, history.Count, balance)), history, balance, stake, SimulatedTournaments)
            : null;

        return new BankrollOverview(
            settings,
            balance,
            sinceStart.Sum(t => t.Profit),
            countedMovements.Sum(m => m.Signed),
            sinceStart.Count,
            unknown,
            new BuyInLimit(maxBuyIn, recentAverage, aboveLimit, window.Count),
            curve,
            movements,
            RiskSimulation.Roi(history),
            risk);
    }

    public Task SaveSettingsAsync(Guid userId, BankrollSettings settings, CancellationToken cancellationToken) =>
        store.SaveSettingsAsync(userId, settings, cancellationToken);

    public async Task<(MovementOutcome Outcome, BankrollMovement? Movement)> AddMovementAsync(
        Guid userId,
        BankrollMovementKind kind,
        decimal amount,
        DateTimeOffset occurredAt,
        string? note,
        CancellationToken cancellationToken)
    {
        var settings = await store.GetSettingsAsync(userId, cancellationToken);
        if (settings is null)
        {
            return (MovementOutcome.NotConfigured, null);
        }

        if (occurredAt < settings.StartedOn)
        {
            return (MovementOutcome.BeforeStart, null);
        }

        var movement = new BankrollMovement(
            Guid.CreateVersion7(occurredAt),
            kind,
            Math.Round(amount, 2),
            occurredAt,
            string.IsNullOrWhiteSpace(note) ? null : note.Trim());
        await store.AddMovementAsync(userId, movement, cancellationToken);
        return (MovementOutcome.Added, movement);
    }

    public Task<bool> DeleteMovementAsync(Guid userId, Guid movementId, CancellationToken cancellationToken) =>
        store.DeleteMovementAsync(userId, movementId, cancellationToken);

    private static TournamentMoney ToMoney(TournamentListItem item) =>
        new(item.StartedAt!.Value, item.Result.TotalBuyIn!.Value, item.Result.Profit!.Value, item.Name);

    /// <summary>
    /// Same data, same simulation: the figures must not move on every refresh. Guid.GetHashCode is not
    /// randomized per process, unlike string hashing and HashCode.Combine.
    /// </summary>
    internal static int Seed(Guid userId, int tournaments, decimal balance) =>
        unchecked((userId.GetHashCode() * 397) ^ (tournaments * 31) ^ decimal.ToInt32(decimal.Truncate(balance)));
}
