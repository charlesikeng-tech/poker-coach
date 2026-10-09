using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;
using PokerCoach.Infrastructure.Coaching;
using PokerCoach.Infrastructure.Import;
using PokerCoach.Infrastructure.Persistence;
using PokerCoach.Infrastructure.Poker;
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
            explanations);
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
}
