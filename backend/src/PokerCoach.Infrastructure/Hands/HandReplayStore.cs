using Microsoft.EntityFrameworkCore;
using PokerCoach.Application.Hands;
using PokerCoach.Domain.Poker;
using PokerCoach.Infrastructure.Import;
using PokerCoach.Infrastructure.Persistence;
using PokerCoach.Infrastructure.Poker;
using PokerCoach.Infrastructure.Statistics;

namespace PokerCoach.Infrastructure.Hands;

/// <summary>
/// Two queries: the hand with its tournament (ownership checked in SQL), then the ids of the tournament's
/// hands in play order for the position and the neighbours (a tournament has hundreds of hands, not more).
/// </summary>
internal sealed class HandReplayStore(PokerCoachDbContext db) : IHandReplayStore
{
    public async Task<HandReplaySource?> FindAsync(Guid userId, Guid handId, CancellationToken cancellationToken)
    {
        var row = await (
                from h in db.Set<HandRecord>().AsNoTracking()
                join t in db.Set<TournamentRecord>() on h.TournamentId equals t.Id
                join a in db.Set<PokerAccountRecord>() on h.PokerAccountId equals a.Id
                where h.Id == handId && a.UserId == userId && a.ConfirmedAt != null
                select new
                {
                    h.Id,
                    h.TournamentId,
                    TournamentName = t.Name,
                    h.StartedAt,
                    h.Level,
                    h.SmallBlind,
                    h.BigBlind,
                    h.Ante,
                    h.MaxSeats,
                    h.ButtonSeat,
                    h.HeroCards,
                    h.Details,
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        // Same order as the tournament detail, so "hand 23" means the same hand on both pages.
        var order = await db.Set<HandRecord>().AsNoTracking()
            .Where(h => h.TournamentId == row.TournamentId)
            .OrderBy(h => h.StartedAt)
            .ThenBy(h => h.ExternalHandId)
            .Select(h => h.Id)
            .ToListAsync(cancellationToken);
        var position = order.IndexOf(row.Id);

        var document = HandDetailsDocument.FromJson(row.Details)
            ?? throw new InvalidOperationException("Stored hand details are empty.");

        return new HandReplaySource(
            row.Id,
            row.TournamentId,
            row.TournamentName,
            row.StartedAt,
            row.Level,
            row.SmallBlind,
            row.BigBlind,
            row.Ante,
            row.MaxSeats,
            row.HeroCards,
            HandFactsStore.ToAnalysis(row.ButtonSeat, row.BigBlind, row.Details),
            Cards(document.Board),
            document.ShownCards
                .GroupBy(s => s.Player, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<Card>)Cards(g.First().Cards), StringComparer.Ordinal),
            position + 1,
            order.Count,
            position > 0 ? order[position - 1] : null,
            position < order.Count - 1 ? order[position + 1] : null);
    }

    /// <summary>Unreadable cards are dropped, not guessed: the parser validated them at import.</summary>
    private static List<Card> Cards(IEnumerable<string> texts) =>
        texts.Select(c => Card.TryParse(c, out var card) ? card : (Card?)null)
            .Where(c => c is not null)
            .Select(c => c!.Value)
            .ToList();
}
