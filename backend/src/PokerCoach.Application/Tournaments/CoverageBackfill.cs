using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Tournaments;
using PokerCoach.HandHistories;

namespace PokerCoach.Application.Tournaments;

/// <param name="HeroStack">Null only for legacy rows; treated as unknown (0).</param>
public sealed record StoredHandPoint(DateTimeOffset StartedAt, int Level, string ExternalHandId, long? HeroStack, long NetChips);

public sealed record StaleTournament(Guid TournamentId, PokerRoom Room, SummaryFacts Summary, IReadOnlyList<StoredHandPoint> Hands);

public interface ICoverageStore
{
    /// <summary>
    /// Tournaments whose coverage is missing or out of date (older version, hands added, newer summary),
    /// and whose hands all have current facts (coverage needs each hand's net chips).
    /// </summary>
    Task<IReadOnlyList<StaleTournament>> GetStaleAsync(int coverageVersion, int factsVersion, int batchSize, CancellationToken cancellationToken);

    Task SaveAsync(IReadOnlyList<(Guid TournamentId, TournamentCoverage Coverage)> coverage, int version, CancellationToken cancellationToken);
}

/// <summary>
/// Keeps each tournament's hand coverage up to date, in the background after hand facts. Hand-number
/// gaps need the room's hand ids: read through the room's provider, never parsed here.
/// </summary>
public sealed class CoverageBackfill(ICoverageStore store, IEnumerable<IHandHistoryProvider> providers)
{
    public const int BatchSize = 100;

    private readonly Dictionary<PokerRoom, IHandHistoryProvider> providers = providers.ToDictionary(p => p.Room);

    /// <returns>Tournaments processed; 0 when everything is up to date.</returns>
    public async Task<int> ProcessBatchAsync(int factsVersion, CancellationToken cancellationToken)
    {
        var stale = await store.GetStaleAsync(TournamentCoverage.Version, factsVersion, BatchSize, cancellationToken);
        if (stale.Count == 0)
        {
            return 0;
        }

        var computed = stale
            .Select(t => (t.TournamentId, TournamentCoverage.Analyze(t.Hands.Select(h => ToPoint(t.Room, h)).ToList(), t.Summary)))
            .ToList();
        await store.SaveAsync(computed, TournamentCoverage.Version, cancellationToken);
        return computed.Count;
    }

    private HandPoint ToPoint(PokerRoom room, StoredHandPoint hand)
    {
        TableSequence? sequence = null;
        var known = providers.TryGetValue(room, out var provider) && provider.TryReadTableSequence(hand.ExternalHandId, out sequence);
        return new HandPoint(
            hand.StartedAt,
            hand.Level,
            known ? sequence!.TableKey : null,
            known ? sequence!.HandNumber : null,
            hand.HeroStack ?? 0,
            hand.NetChips);
    }
}
