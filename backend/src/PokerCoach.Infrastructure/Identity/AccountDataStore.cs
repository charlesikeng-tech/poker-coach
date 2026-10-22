using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Notifications;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Infrastructure.Bankroll;
using PokerCoach.Infrastructure.Coaching;
using PokerCoach.Infrastructure.Import;
using PokerCoach.Infrastructure.Persistence;
using PokerCoach.Infrastructure.Platform;
using PokerCoach.Infrastructure.Poker;
using PokerCoach.Infrastructure.Progress;
using PokerCoach.Infrastructure.Statistics;
using PokerCoach.Infrastructure.Training;

namespace PokerCoach.Infrastructure.Identity;

internal sealed class AccountDataStore(PokerCoachDbContext db) : IAccountDataStore
{
    public async Task<AccountSnapshot?> GetSnapshotAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var identities = await db.Set<ExternalIdentity>().AsNoTracking()
            .Where(i => i.UserId == userId)
            .Select(i => new ExportedIdentity(i.Provider, i.ProviderSubjectId, i.CreatedAt))
            .ToListAsync(cancellationToken);
        var accounts = await db.Set<PokerAccountRecord>().AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new { a.Room, a.ScreenName, a.CreatedAt, a.ConfirmedAt })
            .ToListAsync(cancellationToken);
        var attempts = await db.Set<OpeningAttemptRecord>().AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
        var explanations = await db.Set<LeakExplanationRecord>().AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new ExportedExplanation(e.CreatedAt, e.Language, e.Model, e.Payload))
            .ToListAsync(cancellationToken);
        var reports = await db.Set<CoachingReportRecord>().AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new ExportedReport(r.Kind, r.Subject, r.CreatedAt, r.Language, r.Model, r.Payload))
            .ToListAsync(cancellationToken);

        return new AccountSnapshot(
            user.DisplayName,
            user.Email,
            user.PreferredLanguage,
            user.CreatedAt,
            identities,
            accounts.Select(a => new ExportedPokerAccount(a.Room.ToString(), a.ScreenName, a.CreatedAt, a.ConfirmedAt)).ToList(),
            attempts.Select(a => new ExportedDrillAttempt(
                a.CreatedAt,
                a.Format.ToString(),
                a.Band.ToString(),
                a.Position.ToString(),
                a.Shover?.ToString(),
                a.PushStack,
                a.Hand,
                a.Answer.ToString(),
                a.Correct)).ToList(),
            explanations,
            reports);
    }

    public async IAsyncEnumerable<ExportedUpload> StreamUploadsAsync(Guid userId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Streamed row by row: a player's uploads can weigh hundreds of megabytes.
        var rows = db.Set<ImportedFileRecord>().AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.CreatedAt)
            .Select(f => new ExportedUpload(f.FileName, f.CreatedAt, f.ContentGzip))
            .AsAsyncEnumerable();
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            yield return row;
        }
    }

    /// <summary>
    /// One statement: the foreign keys cascade to poker accounts, tournaments, hands, facts, uploads,
    /// bankroll, training, plans and explanations. AI usage rows keep their cost with the user removed
    /// (the monthly spending cap needs them), and hold nothing personal.
    /// </summary>
    public async Task<bool> DeleteUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Set<User>().Where(u => u.Id == userId).ExecuteDeleteAsync(cancellationToken) > 0;

    /// <summary>
    /// Deletes all poker data but keeps the user, identities, subscription and poker accounts.
    /// Hands and tournaments cascade to facts, entries and coverage.
    /// </summary>
    public async Task ResetPokerDataAsync(Guid userId, CancellationToken cancellationToken)
    {
        var accountIds = await db.Set<PokerAccountRecord>()
            .Where(a => a.UserId == userId)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        if (accountIds.Count > 0)
        {
            await db.Set<HandRecord>().Where(h => accountIds.Contains(h.PokerAccountId)).ExecuteDeleteAsync(cancellationToken);
            await db.Set<TournamentRecord>().Where(t => accountIds.Contains(t.PokerAccountId)).ExecuteDeleteAsync(cancellationToken);
        }

        await db.Set<ImportedFileRecord>().Where(f => f.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<LeakExplanationRecord>().Where(e => e.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<CoachingReportRecord>().Where(r => r.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<OpeningAttemptRecord>().Where(a => a.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<SizingAttemptRecord>().Where(a => a.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<BankrollSettingsRecord>().Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<BankrollMovementRecord>().Where(m => m.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<WeeklyPlanRecord>().Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<CustomOpeningRange>().Where(r => r.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<CustomDefenceRange>().Where(r => r.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<WeeklyNotification>().Where(n => n.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<FeatureUsageRecord>().Where(u => u.UserId == userId).ExecuteDeleteAsync(cancellationToken);
    }
}
