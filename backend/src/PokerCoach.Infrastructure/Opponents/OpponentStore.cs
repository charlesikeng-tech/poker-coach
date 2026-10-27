using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PokerCoach.Application.Opponents;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Infrastructure.Persistence;
using PokerCoach.Infrastructure.Poker;
using PokerCoach.Infrastructure.Statistics;

namespace PokerCoach.Infrastructure.Opponents;

internal sealed class OpponentStore(PokerCoachDbContext db) : IOpponentStore
{
    private const int MaxTags = 10;
    private const int MaxTagLength = 50;

    public async Task<OpponentListResult> ListAsync(
        Guid userId,
        string? search,
        OpponentSort sort,
        bool descending,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var accountIds = await db.Set<PokerAccountRecord>()
            .Where(a => a.UserId == userId)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        if (accountIds.Count == 0)
            return new OpponentListResult([], 0);

        var query = db.Set<OpponentStatsRecord>().AsNoTracking()
            .Where(o => accountIds.Contains(o.PokerAccountId));

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(o => o.OpponentName.Contains(search));

        var totalCount = await query.CountAsync(cancellationToken);

        var sorted = sort switch
        {
            OpponentSort.Name => descending ? query.OrderByDescending(o => o.OpponentName) : query.OrderBy(o => o.OpponentName),
            OpponentSort.LastSeen => descending ? query.OrderByDescending(o => o.LastSeenAt) : query.OrderBy(o => o.LastSeenAt),
            OpponentSort.Vpip => descending
                ? query.OrderByDescending(o => o.Hands > 0 ? (decimal)o.Vpip / o.Hands : 0)
                : query.OrderBy(o => o.Hands > 0 ? (decimal)o.Vpip / o.Hands : 0),
            _ => descending ? query.OrderByDescending(o => o.Hands) : query.OrderBy(o => o.Hands),
        };

        var accountMap = await db.Set<PokerAccountRecord>()
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a, cancellationToken);

        var rows = await sorted
            .ThenBy(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(r =>
        {
            var stats = ToStats(r);
            var account = accountMap[r.PokerAccountId];
            return new OpponentListItem(
                r.Id,
                r.OpponentName,
                account.Room,
                account.ScreenName,
                r.Hands,
                stats.Classify(),
                stats.VpipRate,
                stats.PfrRate,
                stats.ThreeBetRate,
                r.Note,
                ParseTags(r.Tags),
                r.FirstSeenAt,
                r.LastSeenAt);
        }).ToList();

        return new OpponentListResult(items, totalCount);
    }

    public async Task<OpponentDetail?> GetAsync(Guid userId, Guid opponentId, CancellationToken cancellationToken)
    {
        var row = await db.Set<OpponentStatsRecord>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == opponentId, cancellationToken);

        if (row is null) return null;

        var account = await db.Set<PokerAccountRecord>().AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == row.PokerAccountId && a.UserId == userId, cancellationToken);

        if (account is null) return null;

        var stats = ToStats(row);
        return new OpponentDetail(
            row.Id,
            row.OpponentName,
            account.Room,
            account.ScreenName,
            stats,
            stats.Classify(),
            row.Note,
            ParseTags(row.Tags),
            row.FirstSeenAt,
            row.LastSeenAt,
            row.DistinctTournaments);
    }

    public async Task<bool> SetNoteAsync(Guid userId, Guid opponentId, string? note, CancellationToken cancellationToken)
    {
        var row = await FindOwnedAsync(userId, opponentId, cancellationToken);
        if (row is null) return false;

        row.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetTagsAsync(Guid userId, Guid opponentId, IReadOnlyList<string> tags, CancellationToken cancellationToken)
    {
        var row = await FindOwnedAsync(userId, opponentId, cancellationToken);
        if (row is null) return false;

        var cleaned = tags
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Select(t => t.Length > MaxTagLength ? t[..MaxTagLength] : t)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTags)
            .ToList();

        row.Tags = cleaned.Count > 0 ? JsonSerializer.Serialize(cleaned) : null;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<OpponentStatsRecord?> FindOwnedAsync(Guid userId, Guid opponentId, CancellationToken cancellationToken)
    {
        var row = await db.Set<OpponentStatsRecord>()
            .FirstOrDefaultAsync(o => o.Id == opponentId, cancellationToken);

        if (row is null) return null;

        var owns = await db.Set<PokerAccountRecord>()
            .AnyAsync(a => a.Id == row.PokerAccountId && a.UserId == userId, cancellationToken);

        return owns ? row : null;
    }

    private static OpponentStats ToStats(OpponentStatsRecord r) =>
        new(r.Hands, r.Vpip, r.Pfr,
            r.RfiOpportunity, r.Rfi,
            r.StealOpportunity, r.Steal,
            r.ThreeBetOpportunity, r.ThreeBet,
            r.FoldToThreeBetOpportunity, r.FoldToThreeBet,
            r.SawFlop,
            r.CbetFlopOpportunity, r.CbetFlop,
            r.FoldToCbetFlopOpportunity, r.FoldToCbetFlop,
            r.WentToShowdown, r.WonAtShowdown,
            r.PostflopAggressive, r.PostflopDecisions);

    private static IReadOnlyList<string> ParseTags(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }
}
