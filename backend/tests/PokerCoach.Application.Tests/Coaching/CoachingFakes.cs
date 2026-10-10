using PokerCoach.Application.Coaching;
using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Application.Tests.Coaching;

/// <summary>A model that answers what the test sets, and remembers what it was asked.</summary>
internal sealed class ScriptedModel : ICoachingModel
{
    public bool IsAvailable { get; set; } = true;

    public int Calls { get; private set; }

    public DebriefPrompt? LastDebrief { get; private set; }

    public WeekReviewPrompt? LastReview { get; private set; }

    public TournamentDebrief Debrief { get; set; } = new("headline", "story", [], ["strength"], ["work"]);

    public WeekReview Review { get; set; } = new("headline", "summary", [], ["step"]);

    public Task<ModelExplanation> ExplainAsync(ExplanationPrompt prompt, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ModelAnswer<TournamentDebrief>> DebriefTournamentAsync(DebriefPrompt prompt, CancellationToken cancellationToken)
    {
        Calls++;
        LastDebrief = prompt;
        return Task.FromResult(new ModelAnswer<TournamentDebrief>(Debrief, new ModelUsage("test-model", 7_000, 1_200, 0, 0)));
    }

    public Task<ModelAnswer<WeekReview>> ReviewWeekAsync(WeekReviewPrompt prompt, CancellationToken cancellationToken)
    {
        Calls++;
        LastReview = prompt;
        return Task.FromResult(new ModelAnswer<WeekReview>(Review, new ModelUsage("test-model", 3_000, 800, 0, 0)));
    }
}

/// <summary>The usage ledger only: what the gate reads and writes.</summary>
internal sealed class LedgerStore : ICoachingStore
{
    public List<(string Purpose, ModelUsage Usage)> Usage { get; } = [];

    public int CallsToday { get; set; }

    public Task<StoredExplanation?> FindExplanationAsync(Guid userId, string fingerprint, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SaveExplanationAsync(Guid userId, string fingerprint, string language, StoredExplanation explanation, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<ExampleHand>> FindExampleHandsAsync(Guid userId, LeakSituation situation, int count, int factsVersion, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task RecordUsageAsync(Guid? userId, string purpose, ModelUsage usage, decimal costUsd, CancellationToken cancellationToken)
    {
        Usage.Add((purpose, usage));
        return Task.CompletedTask;
    }

    public Task<decimal> SpendSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) => Task.FromResult(0m);

    public Task<int> CountCallsSinceAsync(Guid userId, string purpose, DateTimeOffset since, CancellationToken cancellationToken) => Task.FromResult(CallsToday);
}

internal sealed class InMemoryReportStore : ICoachingReportStore
{
    private readonly Dictionary<string, object> reports = [];

    public Task<StoredReport<T>?> FindAsync<T>(Guid userId, string kind, string subject, string language, CancellationToken cancellationToken) =>
        Task.FromResult(reports.GetValueOrDefault($"{userId}|{kind}|{subject}|{language}") as StoredReport<T>);

    public Task SaveAsync<T>(Guid userId, string kind, string subject, string language, StoredReport<T> report, CancellationToken cancellationToken)
    {
        reports[$"{userId}|{kind}|{subject}|{language}"] = report;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ExampleHand>> LoadHandsAsync(Guid userId, IReadOnlyList<Guid> handIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ExampleHand>>([]);
}

/// <summary>Statistics with no leak at all: the debrief's long-term context stays empty.</summary>
internal sealed class NoLeakStats : IStatisticsReadStore
{
    public Task<IReadOnlyList<(PokerPosition? Position, HeroStatCounts Counts)>> CountByPositionAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<(PokerPosition?, HeroStatCounts)>>([]);

    public Task<StatisticsSample> CountTournamentsAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
        Task.FromResult(new StatisticsSample(0, 0));

    public Task<FormatCounts> CountByFormatAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
        Task.FromResult(new FormatCounts(0, 0));

    public Task<IReadOnlyList<(DateOnly Month, HeroStatCounts Counts)>> CountByMonthAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<(DateOnly, HeroStatCounts)>>([]);

    public Task<int> CountPendingAsync(Guid userId, int factsVersion, CancellationToken cancellationToken) => Task.FromResult(0);
}
