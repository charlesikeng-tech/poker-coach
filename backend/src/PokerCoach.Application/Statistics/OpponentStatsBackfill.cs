using PokerCoach.Application.Import;
using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Application.Statistics;

/// <summary>
/// Backfills opponent stats for poker accounts that have hands but no opponent data (imported before
/// the opponent_stats table existed). Processes one account per call, by lots of hands. Idempotent:
/// uses the same additive upsert as the import pipeline, preceded by a delete of the account's
/// existing stats to guarantee a clean slate.
/// </summary>
public sealed class OpponentStatsBackfill(IOpponentStatsBackfillStore store, IImportStore importStore)
{
    public const int HandBatchSize = 1000;

    /// <returns>Number of hands processed; 0 when no account needs backfill.</returns>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var accountId = await store.GetNextAccountToBackfillAsync(cancellationToken);
        if (accountId is null) return 0;

        await store.DeleteStatsForAccountAsync(accountId.Value, cancellationToken);

        var total = 0;
        int processed;
        Guid? afterHandId = null;
        do
        {
            var hands = await store.GetHandsAsync(accountId.Value, afterHandId, HandBatchSize, cancellationToken);
            if (hands.Count == 0) break;

            var stats = AggregateOpponentFacts(accountId.Value, hands);
            if (stats.Count > 0)
                await importStore.UpsertOpponentStatsAsync(stats, cancellationToken);

            processed = hands.Count;
            total += processed;
            afterHandId = hands[^1].HandId;
        } while (processed == HandBatchSize);

        await store.MarkAccountBackfilledAsync(accountId.Value, cancellationToken);
        return total;
    }

    private static List<OpponentStatsToUpsert> AggregateOpponentFacts(Guid pokerAccountId, IReadOnlyList<BackfillHand> hands)
    {
        var agg = new Dictionary<string, Agg>(StringComparer.Ordinal);

        foreach (var item in hands)
        {
            var facts = OpponentFactsExtractor.Extract(item.Hand);
            foreach (var (opponent, f) in facts)
            {
                if (!agg.TryGetValue(opponent, out var a))
                {
                    a = new Agg();
                    agg[opponent] = a;
                }

                a.Hands++;
                a.Vpip += f.Vpip ? 1 : 0;
                a.Pfr += f.Pfr ? 1 : 0;
                a.RfiOpp += f.RfiOpportunity ? 1 : 0;
                a.Rfi += f.Rfi ? 1 : 0;
                a.StealOpp += f.StealOpportunity ? 1 : 0;
                a.Steal += f.Steal ? 1 : 0;
                a.ThreeBetOpp += f.ThreeBetOpportunity ? 1 : 0;
                a.ThreeBet += f.ThreeBet ? 1 : 0;
                a.FoldToThreeBetOpp += f.FoldToThreeBetOpportunity ? 1 : 0;
                a.FoldToThreeBet += f.FoldToThreeBet ? 1 : 0;
                a.SawFlop += f.SawFlop ? 1 : 0;
                a.CbetFlopOpp += f.CbetFlopOpportunity ? 1 : 0;
                a.CbetFlop += f.CbetFlop ? 1 : 0;
                a.FoldToCbetOpp += f.FoldToCbetFlopOpportunity ? 1 : 0;
                a.FoldToCbet += f.FoldToCbetFlop ? 1 : 0;
                a.Wtsd += f.WentToShowdown ? 1 : 0;
                a.Wsd += f.WonAtShowdown ? 1 : 0;
                a.Aggressive += f.PostflopAggressive;
                a.Decisions += f.PostflopDecisions;

                if (a.FirstSeen == DateTimeOffset.MinValue || item.StartedAt < a.FirstSeen)
                    a.FirstSeen = item.StartedAt;
                if (item.StartedAt > a.LastSeen)
                    a.LastSeen = item.StartedAt;
                if (item.TournamentExternalId is not null)
                    a.Tournaments.Add(item.TournamentExternalId);
            }
        }

        return agg.Select(kv => new OpponentStatsToUpsert(
            pokerAccountId, kv.Key,
            kv.Value.Hands, kv.Value.Vpip, kv.Value.Pfr,
            kv.Value.RfiOpp, kv.Value.Rfi,
            kv.Value.StealOpp, kv.Value.Steal,
            kv.Value.ThreeBetOpp, kv.Value.ThreeBet,
            kv.Value.FoldToThreeBetOpp, kv.Value.FoldToThreeBet,
            kv.Value.SawFlop,
            kv.Value.CbetFlopOpp, kv.Value.CbetFlop,
            kv.Value.FoldToCbetOpp, kv.Value.FoldToCbet,
            kv.Value.Wtsd, kv.Value.Wsd,
            kv.Value.Aggressive, kv.Value.Decisions,
            kv.Value.FirstSeen, null)).ToList();
    }

    private sealed class Agg
    {
        public int Hands, Vpip, Pfr;
        public int RfiOpp, Rfi, StealOpp, Steal;
        public int ThreeBetOpp, ThreeBet, FoldToThreeBetOpp, FoldToThreeBet;
        public int SawFlop, CbetFlopOpp, CbetFlop, FoldToCbetOpp, FoldToCbet;
        public int Wtsd, Wsd, Aggressive, Decisions;
        public DateTimeOffset FirstSeen, LastSeen;
        public HashSet<string> Tournaments = new(StringComparer.Ordinal);
    }
}

public sealed record BackfillHand(Guid HandId, HandForAnalysis Hand, DateTimeOffset StartedAt, string? TournamentExternalId);

public interface IOpponentStatsBackfillStore
{
    /// <summary>A poker account that has hands but needs opponent stats (re)computed. Null when done.</summary>
    Task<Guid?> GetNextAccountToBackfillAsync(CancellationToken cancellationToken);

    Task DeleteStatsForAccountAsync(Guid pokerAccountId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BackfillHand>> GetHandsAsync(Guid pokerAccountId, Guid? afterHandId, int batchSize, CancellationToken cancellationToken);

    Task MarkAccountBackfilledAsync(Guid pokerAccountId, CancellationToken cancellationToken);
}
