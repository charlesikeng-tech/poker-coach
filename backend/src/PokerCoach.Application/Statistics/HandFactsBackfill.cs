using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Application.Statistics;

public sealed record PendingHand(Guid HandId, HandForAnalysis Hand);

public interface IHandFactsStore
{
    /// <summary>Hands without facts, or with facts computed by an older <see cref="HeroHandFacts.Version"/>.</summary>
    Task<IReadOnlyList<PendingHand>> GetPendingAsync(int version, int batchSize, CancellationToken cancellationToken);

    /// <summary>Insert or replace, in one transaction.</summary>
    Task SaveAsync(IReadOnlyList<(Guid HandId, HeroHandFacts Facts)> facts, int version, CancellationToken cancellationToken);
}

/// <summary>
/// Keeps per-hand facts in step with the hands (ADR-0006): computes them for new hands and recomputes
/// them all when a definition changes (version bump). Runs in the background worker; idempotent.
/// </summary>
public sealed class HandFactsBackfill(IHandFactsStore store)
{
    public const int BatchSize = 500;

    /// <returns>Number of hands processed; 0 when everything is up to date.</returns>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var pending = await store.GetPendingAsync(HeroHandFacts.Version, BatchSize, cancellationToken);
        if (pending.Count == 0)
        {
            return 0;
        }

        var facts = pending.Select(p => (p.HandId, HandAnalyzer.Analyze(p.Hand))).ToList();
        await store.SaveAsync(facts, HeroHandFacts.Version, cancellationToken);
        return facts.Count;
    }
}
