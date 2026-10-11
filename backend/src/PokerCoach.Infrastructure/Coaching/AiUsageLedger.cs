using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PokerCoach.Application.Coaching;
using PokerCoach.Domain.Identity;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.Infrastructure.Coaching;

/// <summary>
/// The AI usage ledger in PostgreSQL (ADR-0017). Reservations are serialised by one transaction-scoped advisory
/// lock: the read of the usage and the insert of the reservation cannot interleave between two requests, so
/// neither a user's credits nor the global budget can be overrun by concurrent calls. The lock is held for a
/// few milliseconds (a handful of indexed reads and one insert), never during the provider call.
/// </summary>
internal sealed partial class AiUsageLedger(PokerCoachDbContext db, ILogger<AiUsageLedger> logger) : IAiUsageLedger
{
    /// <summary>Arbitrary, unique to this lock in the database.</summary>
    private const long ReservationLock = 7_206_031_178_400_017;

    public async Task<CoachingFailure?> ReserveAsync(AiReservationRequest request, Func<AiUsageSnapshot, CoachingFailure?> decide, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decide);
        var record = new ModelUsageRecord
        {
            Id = request.Id,
            UserId = request.UserId,
            Purpose = request.Task.Purpose(),
            Status = AiUsageStatus.Reserved,
            Credits = request.Credits,
            ReservedCostUsd = request.CeilingUsd,
            UsageKnown = true,
            CreatedAt = request.Now,
        };

        // The whole block is retried on a transient failure: the record is created once, outside it.
        var strategy = db.Database.CreateExecutionStrategy();
        var (refused, spendBefore) = await strategy.ExecuteAsync(async ct =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({ReservationLock})", ct);

            var snapshot = await SnapshotAsync(request, ct);
            if (decide(snapshot) is { } failure)
            {
                return (failure, snapshot.SpendThisMonthUsd);
            }

            db.Entry(record).State = EntityState.Added;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            db.Entry(record).State = EntityState.Detached;
            return ((CoachingFailure?)null, snapshot.SpendThisMonthUsd);
        }, cancellationToken);

        if (refused is null && spendBefore < request.AlertAtUsd && spendBefore + request.CeilingUsd >= request.AlertAtUsd)
        {
            LogBudgetAlert(logger, spendBefore + request.CeilingUsd, request.AlertAtUsd);
        }
        else if (refused == CoachingFailure.BudgetExhausted)
        {
            LogBudgetExhausted(logger, spendBefore, request.Task.Purpose());
        }

        return refused;
    }

    public async Task SettleAsync(Guid reservationId, AiSettlement settlement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        var status = settlement.Succeeded ? AiUsageStatus.Billed : AiUsageStatus.Failed;
        var pending = db.Set<ModelUsageRecord>().Where(u => u.Id == reservationId && u.Status == AiUsageStatus.Reserved);

        int updated;
        if (settlement.Usage is { } usage && settlement.CostUsd is { } cost)
        {
            updated = await pending.ExecuteUpdateAsync(
                set => set
                    .SetProperty(u => u.Status, status)
                    .SetProperty(u => u.Credits, settlement.Credits)
                    .SetProperty(u => u.Model, usage.Model)
                    .SetProperty(u => u.InputTokens, usage.InputTokens)
                    .SetProperty(u => u.OutputTokens, usage.OutputTokens)
                    .SetProperty(u => u.CacheWriteTokens, usage.CacheWriteTokens)
                    .SetProperty(u => u.CacheReadTokens, usage.CacheReadTokens)
                    .SetProperty(u => u.CostUsd, cost)
                    .SetProperty(u => u.UsageKnown, true)
                    .SetProperty(u => u.SettledAt, settlement.At),
                cancellationToken);
        }
        else
        {
            // Unknown billing: the reserved ceiling stays as the cost, never a guess below it.
            updated = await pending.ExecuteUpdateAsync(
                set => set
                    .SetProperty(u => u.Status, status)
                    .SetProperty(u => u.Credits, settlement.Credits)
                    .SetProperty(u => u.CostUsd, u => u.ReservedCostUsd)
                    .SetProperty(u => u.UsageKnown, false)
                    .SetProperty(u => u.SettledAt, settlement.At),
                cancellationToken);
        }

        if (updated == 0)
        {
            LogSettledTwice(logger, reservationId);
        }
    }

    public Task<int> CreditsUsedAsync(Guid userId, DateTimeOffset since, DateTimeOffset liveAfter, CancellationToken cancellationToken) =>
        db.Set<ModelUsageRecord>()
            .Where(u => u.UserId == userId && u.CreatedAt >= since)
            .Where(u => u.Status == AiUsageStatus.Billed || (u.Status == AiUsageStatus.Reserved && u.CreatedAt >= liveAfter))
            .SumAsync(u => u.Credits, cancellationToken);

    public async Task<AiUsageReport> ReportAsync(DateTimeOffset since, int topUsers, CancellationToken cancellationToken)
    {
        var month = db.Set<ModelUsageRecord>().AsNoTracking().Where(u => u.CreatedAt >= since);
        var groups = await month
            .GroupBy(u => new { u.Purpose, u.Status, u.UsageKnown })
            .Select(g => new
            {
                g.Key.Purpose,
                g.Key.Status,
                g.Key.UsageKnown,
                Calls = g.Count(),
                Credits = g.Sum(u => u.Credits),
                Cost = g.Sum(u => u.CostUsd),
                Reserved = g.Sum(u => u.ReservedCostUsd),
            })
            .ToListAsync(cancellationToken);

        var tasks = Enum.GetValues<AiTask>()
            .Select(task =>
            {
                var rows = groups.Where(g => g.Purpose == task.Purpose()).ToList();
                var settled = rows.Where(g => g.Status != AiUsageStatus.Reserved).ToList();
                return new AiTaskUsage(
                    task,
                    Billed: rows.Where(g => g.Status == AiUsageStatus.Billed).Sum(g => g.Calls),
                    Failed: rows.Where(g => g.Status == AiUsageStatus.Failed).Sum(g => g.Calls),
                    UnknownCost: settled.Where(g => !g.UsageKnown).Sum(g => g.Calls),
                    Credits: rows.Where(g => g.Status == AiUsageStatus.Billed).Sum(g => g.Credits),
                    CostUsd: settled.Sum(g => g.Cost));
            })
            .ToList();
        var open = groups.Where(g => g.Status == AiUsageStatus.Reserved).Sum(g => g.Reserved);

        var top = await month
            .Where(u => u.Status != AiUsageStatus.Reserved)
            .GroupBy(u => u.UserId)
            .Select(g => new { UserId = g.Key, Credits = g.Sum(u => u.Credits), Cost = g.Sum(u => u.CostUsd) })
            .OrderByDescending(g => g.Cost)
            .Take(topUsers)
            .ToListAsync(cancellationToken);
        var ids = top.Where(t => t.UserId is not null).Select(t => t.UserId!.Value).ToList();
        var emails = await db.Set<User>().AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Email })
            .ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);

        return new AiUsageReport(
            tasks.Sum(t => t.CostUsd) + open,
            open,
            tasks,
            top.Select(t => new AiUserUsage(t.UserId, t.UserId is { } id ? emails.GetValueOrDefault(id) : null, t.Credits, t.Cost)).ToList());
    }

    private async Task<AiUsageSnapshot> SnapshotAsync(AiReservationRequest request, CancellationToken cancellationToken)
    {
        var purpose = request.Task.Purpose();
        var month = db.Set<ModelUsageRecord>().AsNoTracking().Where(u => u.CreatedAt >= request.MonthStart);
        var counted = month.Where(u => u.Status != AiUsageStatus.Reserved || u.CreatedAt >= request.LiveAfter);
        var mine = counted.Where(u => u.UserId == request.UserId);

        var credits = await mine.Where(u => u.Status != AiUsageStatus.Failed).SumAsync(u => u.Credits, cancellationToken);
        var today = await mine.CountAsync(u => u.Purpose == purpose && u.CreatedAt >= request.DayStart, cancellationToken);
        var thisMonth = await mine.CountAsync(u => u.Purpose == purpose && u.Status != AiUsageStatus.Failed, cancellationToken);

        // Abandoned reservations keep counting at their ceiling: whether the provider billed them is unknown.
        var spend = await month.SumAsync(u => u.Status == AiUsageStatus.Reserved ? u.ReservedCostUsd : u.CostUsd, cancellationToken);
        return new AiUsageSnapshot(credits, today, thisMonth, spend);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI budget alert: this month's spend reaches {SpendUsd} USD, past the {AlertUsd} USD alert threshold.")]
    private static partial void LogBudgetAlert(ILogger logger, decimal spendUsd, decimal alertUsd);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI budget exhausted: {Purpose} refused at {SpendUsd} USD spent this month.")]
    private static partial void LogBudgetExhausted(ILogger logger, decimal spendUsd, string purpose);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI usage reservation {ReservationId} was already settled or does not exist.")]
    private static partial void LogSettledTwice(ILogger logger, Guid reservationId);
}
