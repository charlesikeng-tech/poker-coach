using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;

namespace PokerCoach.Application.Leaks;

/// <param name="Hands">Hands the detection looked at (15+ big blinds).</param>
/// <param name="PendingHands">Hands still being analyzed: the result may change once they are.</param>
public sealed record LeakAnalysis(LeakReport Report, int Hands, StatisticsSample Sample, int PendingHands, int ReferenceVersion);

/// <summary>Leak detection (ADR-0007): the hero's statistics against reference ranges, with a sample guard.</summary>
public sealed class LeakService(IStatisticsReadStore store)
{
    /// <param name="to">Exclusive upper bound.</param>
    public async Task<LeakAnalysis> GetAsync(Guid userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var filter = new StatisticsFilter(from, to, ReferenceRanges.MinStackBigBlinds, null);
        var groups = await store.CountByPositionAsync(userId, filter, HeroHandFacts.Version, cancellationToken);
        var sample = await store.CountTournamentsAsync(userId, filter, HeroHandFacts.Version, cancellationToken);
        var pending = await store.CountPendingAsync(userId, HeroHandFacts.Version, cancellationToken);

        var overall = groups.Aggregate(HeroStatCounts.Zero, (total, g) => total.Add(g.Counts));
        var byPosition = groups
            .Where(g => g.Position is not null)
            .ToDictionary(g => g.Position!.Value, g => g.Counts);

        var report = LeakDetector.Detect(overall, byPosition, ReferenceRanges.LowStakesMtt);
        return new LeakAnalysis(report, overall.Hands, sample, pending, ReferenceRanges.Version);
    }
}
