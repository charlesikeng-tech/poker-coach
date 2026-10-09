using Microsoft.EntityFrameworkCore;
using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Infrastructure.Import;
using PokerCoach.Infrastructure.Persistence;
using PokerCoach.Infrastructure.Poker;

namespace PokerCoach.Infrastructure.Statistics;

internal sealed class HandFactsStore(PokerCoachDbContext db) : IHandFactsStore
{
    public async Task<IReadOnlyList<PendingHand>> GetPendingAsync(int version, int batchSize, CancellationToken cancellationToken)
    {
        var rows = await (
                from h in db.Set<HandRecord>().AsNoTracking()
                join f in db.Set<HandHeroFactsRecord>() on h.Id equals f.HandId into facts
                from f in facts.DefaultIfEmpty()
                where f == null || f.FactsVersion < version
                orderby h.Id
                select new { h.Id, h.ButtonSeat, h.BigBlind, h.Details, h.HeroCards })
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        return rows.Select(r => new PendingHand(r.Id, ToAnalysis(r.ButtonSeat, r.BigBlind, r.Details, r.HeroCards))).ToList();
    }

    public async Task SaveAsync(IReadOnlyList<(Guid HandId, HeroHandFacts Facts)> facts, int version, CancellationToken cancellationToken)
    {
        var ids = facts.Select(f => f.HandId).ToList();
        var existing = await db.Set<HandHeroFactsRecord>()
            .Where(f => ids.Contains(f.HandId))
            .ToDictionaryAsync(f => f.HandId, cancellationToken);

        foreach (var (handId, handFacts) in facts)
        {
            if (!existing.TryGetValue(handId, out var record))
            {
                record = new HandHeroFactsRecord { HandId = handId };
                db.Add(record);
            }

            record.Apply(handFacts, version);
        }

        // One SaveChanges: one transaction. A concurrent worker on the same batch fails on the primary
        // key and its batch is simply picked up again.
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    /// <summary>Stored hand document to the analysis input. Imported hands always have a hero.</summary>
    internal static HandForAnalysis ToAnalysis(int buttonSeat, long bigBlind, string details, string? heroCards = null)
    {
        var document = HandDetailsDocument.FromJson(details)
            ?? throw new InvalidOperationException("Stored hand details are empty.");
        var hero = document.HeroName
            ?? throw new InvalidOperationException("Stored hand has no hero; the import never stores such hands.");

        return new HandForAnalysis(
            buttonSeat,
            bigBlind,
            hero,
            document.Seats.Select(s => new SeatState(s.Number, s.Player, s.Stack)).ToList(),
            document.Actions
                .Select(a => new HandAction(a.Street, a.Player, HandForAnalysisMapper.Map(a.Type), a.Amount, a.IsAllIn))
                .ToList(),
            document.Collections
                .GroupBy(c => c.Player, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Sum(c => c.Amount), StringComparer.Ordinal),
            HandForAnalysisMapper.KnownCards(
                hero,
                ParseCards(heroCards is null ? [] : Enumerable.Range(0, heroCards.Length / 2).Select(i => heroCards.Substring(i * 2, 2))),
                document.ShownCards.Select(s => (s.Player, ParseCards(s.Cards)))));
    }

    /// <summary>Stored card texts ("Ah"); an unreadable one makes the whole hand unknown (empty).</summary>
    private static IReadOnlyList<Card> ParseCards(IEnumerable<string> texts)
    {
        var cards = new List<Card>();
        foreach (var text in texts)
        {
            if (!Card.TryParse(text, out var card))
            {
                return [];
            }

            cards.Add(card);
        }

        return cards;
    }
}
