using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PokerCoach.Application.Coaching;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Infrastructure.Import;
using PokerCoach.Infrastructure.Persistence;
using PokerCoach.Infrastructure.Poker;
using PokerCoach.Infrastructure.Statistics;

namespace PokerCoach.Infrastructure.Coaching;

internal sealed class CoachingStore(PokerCoachDbContext db, TimeProvider time) : ICoachingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StoredExplanation?> FindExplanationAsync(Guid userId, string fingerprint, CancellationToken cancellationToken)
    {
        var payload = await db.Set<LeakExplanationRecord>().AsNoTracking()
            .Where(e => e.UserId == userId && e.Fingerprint == fingerprint)
            .Select(e => e.Payload)
            .FirstOrDefaultAsync(cancellationToken);
        return payload is null ? null : JsonSerializer.Deserialize<StoredExplanation>(payload, JsonOptions);
    }

    public async Task SaveExplanationAsync(Guid userId, string fingerprint, string language, StoredExplanation explanation, CancellationToken cancellationToken)
    {
        db.Add(new LeakExplanationRecord
        {
            Id = Guid.CreateVersion7(time.GetUtcNow()),
            UserId = userId,
            Fingerprint = fingerprint,
            Language = language,
            Payload = JsonSerializer.Serialize(explanation, JsonOptions),
            Model = explanation.Model,
            CreatedAt = explanation.CreatedAt,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Two identical requests at once: the first one stored it, which is all that matters.
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyList<ExampleHand>> FindExampleHandsAsync(
        Guid userId,
        LeakSituation situation,
        int count,
        int factsVersion,
        CancellationToken cancellationToken)
    {
        var facts = db.Set<HandHeroFactsRecord>().AsNoTracking()
            .Where(f => f.FactsVersion == factsVersion && f.StackInBigBlinds >= situation.MinStackBigBlinds)
            .Where(Spot(situation.Stat, situation.Direction));
        var sixMax = situation.Format == TableFormat.SixMax;

        // Positions are named within the format (OpeningSeat), which SQL does not know: recent candidates
        // are read with what the renaming needs, then filtered here. Recent spots are plentiful.
        var candidates = await (
                from f in facts
                join h in db.Set<HandRecord>() on f.HandId equals h.Id
                join a in db.Set<PokerAccountRecord>() on h.PokerAccountId equals a.Id
                where a.UserId == userId && a.ConfirmedAt != null && (h.MaxSeats <= 6) == sixMax
                orderby h.StartedAt descending
                select new { f.Position, f.PlayersDealt, h.Id })
            .Take(situation.Position is null ? count : count * 40)
            .ToListAsync(cancellationToken);
        var ids = candidates
            .Where(c => situation.Position is not { } wanted
                || (c.Position is { } p && OpeningSeat.Canonical(p, c.PlayersDealt, situation.Format) == wanted))
            .Take(count)
            .Select(c => c.Id)
            .ToList();

        var rows = await db.Set<HandRecord>().AsNoTracking()
            .Where(h => ids.Contains(h.Id))
            .OrderByDescending(h => h.StartedAt)
            .Select(h => new { h.Id, h.StartedAt, h.Level, h.Ante, h.ButtonSeat, h.BigBlind, h.HeroCards, h.Details })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r =>
            {
                var document = HandDetailsDocument.FromJson(r.Details);
                var board = (document?.Board ?? [])
                    .Select(c => Card.TryParse(c, out var card) ? card : (Card?)null)
                    .Where(c => c is not null)
                    .Select(c => c!.Value)
                    .ToList();
                return new ExampleHand(r.Id, r.StartedAt, r.Level, r.Ante, HandFactsStore.ToAnalysis(r.ButtonSeat, r.BigBlind, r.Details), board, r.HeroCards);
            })
            .ToList();
    }

    public async Task RecordUsageAsync(Guid? userId, string purpose, ModelUsage usage, decimal costUsd, CancellationToken cancellationToken)
    {
        db.Add(new ModelUsageRecord
        {
            Id = Guid.CreateVersion7(time.GetUtcNow()),
            UserId = userId,
            Purpose = purpose,
            Model = usage.Model,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            CacheWriteTokens = usage.CacheWriteTokens,
            CacheReadTokens = usage.CacheReadTokens,
            CostUsd = costUsd,
            CreatedAt = time.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    public async Task<decimal> SpendSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
        await db.Set<ModelUsageRecord>().Where(u => u.CreatedAt >= since).SumAsync(u => u.CostUsd, cancellationToken);

    public Task<int> CountCallsSinceAsync(Guid userId, string purpose, DateTimeOffset since, CancellationToken cancellationToken) =>
        db.Set<ModelUsageRecord>().CountAsync(u => u.UserId == userId && u.Purpose == purpose && u.CreatedAt >= since, cancellationToken);

    /// <summary>Too high: hands where the player did it. Too low: hands where he could have and did not.</summary>
    private static Expression<Func<HandHeroFactsRecord, bool>> Spot(LeakStat stat, LeakDirection direction)
    {
        var tooHigh = direction == LeakDirection.TooHigh;
        return stat switch
        {
            LeakStat.Vpip => tooHigh ? f => f.Vpip : f => f.HadPreflopDecision && !f.Vpip,
            LeakStat.Pfr => tooHigh ? f => f.Pfr : f => f.HadPreflopDecision && !f.Pfr,
            LeakStat.VpipPfrGap => tooHigh ? f => f.Vpip && !f.Pfr : f => f.Pfr,
            LeakStat.Rfi => tooHigh ? f => f.Rfi : f => f.RfiOpportunity && !f.Rfi,
            LeakStat.Limp => tooHigh ? f => f.Limp : f => f.RfiOpportunity && !f.Limp,
            LeakStat.Steal => tooHigh ? f => f.Steal : f => f.StealOpportunity && !f.Steal,
            LeakStat.ThreeBet => tooHigh ? f => f.ThreeBet : f => f.ThreeBetOpportunity && !f.ThreeBet,
            LeakStat.FoldToThreeBet => tooHigh ? f => f.FoldToThreeBet : f => f.FoldToThreeBetOpportunity && !f.FoldToThreeBet,
            LeakStat.CbetFlop => tooHigh ? f => f.CbetFlop : f => f.CbetFlopOpportunity && !f.CbetFlop,
            LeakStat.WentToShowdown => tooHigh ? f => f.WentToShowdown : f => f.SawFlop && !f.WentToShowdown,
            LeakStat.WonAtShowdown => tooHigh ? f => f.WonAtShowdown : f => f.WentToShowdown && !f.WonAtShowdown,
            LeakStat.FoldToCbetFlop => tooHigh ? f => f.FoldToCbetFlop : f => f.FoldToCbetFlopOpportunity && !f.FoldToCbetFlop,
            LeakStat.CbetTurn => tooHigh ? f => f.CbetTurn : f => f.CbetTurnOpportunity && !f.CbetTurn,
            LeakStat.CheckRaiseFlop => tooHigh ? f => f.CheckRaiseFlop : f => f.CheckRaiseFlopOpportunity && !f.CheckRaiseFlop,
            LeakStat.WonWhenSawFlop => tooHigh ? f => f.WonWhenSawFlop : f => f.SawFlop && !f.WonWhenSawFlop,
            LeakStat.PostflopAggression => tooHigh ? f => f.PostflopAggressive > 0 : f => f.PostflopDecisions > 0 && f.PostflopAggressive == 0,
            _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, null),
        };
    }
}
