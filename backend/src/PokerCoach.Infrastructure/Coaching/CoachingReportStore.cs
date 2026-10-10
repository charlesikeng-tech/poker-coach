using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PokerCoach.Application.Coaching;
using PokerCoach.Domain.Poker;
using PokerCoach.Infrastructure.Import;
using PokerCoach.Infrastructure.Persistence;
using PokerCoach.Infrastructure.Poker;
using PokerCoach.Infrastructure.Statistics;

namespace PokerCoach.Infrastructure.Coaching;

internal sealed class CoachingReportStore(PokerCoachDbContext db, TimeProvider time) : ICoachingReportStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<StoredReport<T>?> FindAsync<T>(Guid userId, string kind, string subject, string language, CancellationToken cancellationToken)
    {
        var row = await db.Set<CoachingReportRecord>().AsNoTracking()
            .Where(r => r.UserId == userId && r.Kind == kind && r.Subject == subject && r.Language == language)
            .Select(r => new { r.Payload, r.Fingerprint, r.Model, r.CreatedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null || JsonSerializer.Deserialize<T>(row.Payload, JsonOptions) is not { } report)
        {
            return null;
        }

        return new StoredReport<T>(report, row.Fingerprint, row.Model, row.CreatedAt);
    }

    public async Task SaveAsync<T>(Guid userId, string kind, string subject, string language, StoredReport<T> report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        var payload = JsonSerializer.Serialize(report.Report, JsonOptions);

        // Atomic replace: two generations at once leave one row, the last written.
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO coaching.reports (id, user_id, kind, subject, language, fingerprint, payload, model, created_at)
            VALUES ({Guid.CreateVersion7(time.GetUtcNow())}, {userId}, {kind}, {subject}, {language}, {report.Fingerprint},
                    CAST({payload} AS jsonb), {report.Model}, {report.CreatedAt})
            ON CONFLICT (user_id, kind, subject, language) DO UPDATE
            SET fingerprint = EXCLUDED.fingerprint, payload = EXCLUDED.payload, model = EXCLUDED.model,
                created_at = EXCLUDED.created_at
            """,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ExampleHand>> LoadHandsAsync(Guid userId, IReadOnlyList<Guid> handIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handIds);
        var rows = await (
                from h in db.Set<HandRecord>().AsNoTracking()
                join a in db.Set<PokerAccountRecord>() on h.PokerAccountId equals a.Id
                where a.UserId == userId && handIds.Contains(h.Id)
                select new { h.Id, h.StartedAt, h.Level, h.Ante, h.ButtonSeat, h.BigBlind, h.HeroCards, h.Details })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r =>
            {
                var board = (HandDetailsDocument.FromJson(r.Details)?.Board ?? [])
                    .Select(c => Card.TryParse(c, out var card) ? card : (Card?)null)
                    .OfType<Card>()
                    .ToList();

                // With the hero's cards, the analysis also knows the cards shown at showdown.
                return new ExampleHand(r.Id, r.StartedAt, r.Level, r.Ante, HandFactsStore.ToAnalysis(r.ButtonSeat, r.BigBlind, r.Details, r.HeroCards), board, r.HeroCards);
            })
            .ToList();
    }
}
