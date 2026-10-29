using Microsoft.EntityFrameworkCore;
using PokerCoach.Application.Tournaments;
using PokerCoach.Infrastructure.Coaching;
using PokerCoach.Infrastructure.Import;
using PokerCoach.Infrastructure.Persistence;
using PokerCoach.Infrastructure.Poker;

namespace PokerCoach.Infrastructure.Tournaments;

/// <summary>
/// Deletes a tournament that has no summary (no entries), along with its hands, hand facts,
/// coaching reports and orphaned imported-file records so the same files can be re-imported.
/// All deletions run in a single transaction so they succeed or fail as a whole.
/// </summary>
internal sealed class TournamentDeleteStore(PokerCoachDbContext db) : ITournamentDeleteStore
{
    public async Task<TournamentDeleteResult> DeleteAsync(Guid userId, Guid tournamentId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Ownership check: the tournament must belong to a confirmed poker account of the user.
        var tournament = await (
                from t in db.Set<TournamentRecord>()
                join a in db.Set<PokerAccountRecord>() on t.PokerAccountId equals a.Id
                where t.Id == tournamentId && a.UserId == userId
                select new { t.Id, HasEntries = db.Set<TournamentEntryRecord>().Any(e => e.TournamentId == t.Id) })
            .FirstOrDefaultAsync(cancellationToken);

        if (tournament is null)
        {
            return TournamentDeleteResult.NotFound;
        }

        if (tournament.HasEntries)
        {
            return TournamentDeleteResult.HasSummary;
        }

        // 1. Collect imported-file ids referenced by the tournament's hands (before deleting them).
        var importedFileIds = await db.Set<HandRecord>().AsNoTracking()
            .Where(h => h.TournamentId == tournamentId && h.ImportedFileId != null)
            .Select(h => h.ImportedFileId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        // 2. Delete hands (hand_hero_facts cascade via FK on HandId).
        await db.Set<HandRecord>()
            .Where(h => h.TournamentId == tournamentId)
            .ExecuteDeleteAsync(cancellationToken);

        // 3. Delete imported-file records that no longer have any hands pointing to them,
        //    so the same content can be re-imported.
        if (importedFileIds.Count > 0)
        {
            await db.Set<ImportedFileRecord>()
                .Where(f => importedFileIds.Contains(f.Id)
                    && !db.Set<HandRecord>().Any(h => h.ImportedFileId == f.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        // 4. Delete coaching reports that reference this tournament (no FK, subject is a string).
        var subject = tournamentId.ToString("D");
        await db.Set<CoachingReportRecord>()
            .Where(r => r.UserId == userId && r.Subject == subject)
            .ExecuteDeleteAsync(cancellationToken);

        // 5. Delete the tournament itself (entries and coverage cascade via FK).
        await db.Set<TournamentRecord>()
            .Where(t => t.Id == tournamentId)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return TournamentDeleteResult.Deleted;
    }
}
