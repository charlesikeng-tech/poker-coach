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

    /// <summary>What a call can cost at most; small by default so the budget is not what tests hit.</summary>
    public ModelUsage CeilingUsage { get; set; } = new("test-model", 0, 1_000, 0, 0);

    public ModelUsage Ceiling(AiTask task) => CeilingUsage;

    public Task<ModelAnswer<LeakExplanation>> ExplainAsync(ExplanationPrompt prompt, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ModelAnswer<TournamentDebrief>> DebriefTournamentAsync(DebriefPrompt prompt, CancellationToken cancellationToken)
    {
        Calls++;
        LastDebrief = prompt;
        return Task.FromResult(new ModelAnswer<TournamentDebrief>(Debrief, new ModelUsage("test-model", 7_000, 1_200, 0, 0)));
    }

    public Task<ModelAnswer<TournamentDebrief>> DebriefCashSessionAsync(DebriefPrompt prompt, CancellationToken cancellationToken)
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

    public Task<ModelAnswer<BankrollAdvice>> AdviseOnBankrollAsync(BankrollCoachPrompt prompt, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ModelAnswer<MonthlyReport>> ReviewMonthAsync(MonthlyReportPrompt prompt, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ModelAnswer<TournamentSelectionAdvice>> AdviseTournamentSelectionAsync(TournamentSelectionPrompt prompt, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ModelAnswer<StrategyPlanAdvice>> AdviseStrategyPlanAsync(StrategyPlanPrompt prompt, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ModelAnswer<PlayerProfile>> ProfilePlayerAsync(PlayerProfilePrompt prompt, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>The AI usage ledger in memory, with the same counting rules as the PostgreSQL one (ADR-0017).</summary>
internal sealed class InMemoryAiUsageLedger : IAiUsageLedger
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public List<Entry> Entries { get; } = [];

    private readonly Dictionary<Guid, int> purchasedBalances = [];

    private readonly HashSet<string> processedEvents = [];

    public IEnumerable<Entry> Billed => Entries.Where(e => e.Status == "Billed");

    /// <summary>Billed calls made before the test, at <paramref name="at"/>, costing nothing.</summary>
    public void Seed(Guid userId, AiTask task, int times, DateTimeOffset at)
    {
        for (var i = 0; i < times; i++)
        {
            Entries.Add(new Entry(Guid.NewGuid(), userId, task, at) { Status = "Billed", Credits = task.Credits(), Known = true });
        }
    }

    public async Task<CoachingFailure?> ReserveAsync(AiReservationRequest request, Func<AiUsageSnapshot, CoachingFailure?> decide, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var month = Entries.Where(e => e.CreatedAt >= request.MonthStart).ToList();
            var counted = month.Where(e => e.Status != "Reserved" || e.CreatedAt >= request.LiveAfter).ToList();
            var mine = counted.Where(e => e.UserId == request.UserId).ToList();
            purchasedBalances.TryGetValue(request.UserId, out var purchasedBalance);
            var snapshot = new AiUsageSnapshot(
                mine.Where(e => e.Status != "Failed").Sum(e => e.Credits),
                mine.Count(e => e.Task == request.Task && e.CreatedAt >= request.DayStart),
                mine.Count(e => e.Task == request.Task && e.Status != "Failed"),
                month.Sum(e => e.Status == "Reserved" ? e.ReservedUsd : e.CostUsd),
                purchasedBalance);
            if (decide(snapshot) is { } refused)
            {
                return refused;
            }

            Entries.Add(new Entry(request.Id, request.UserId, request.Task, request.Now) { Credits = request.Credits, ReservedUsd = request.CeilingUsd, Known = true });
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    public Task SettleAsync(Guid reservationId, AiSettlement settlement, CancellationToken cancellationToken)
    {
        var entry = Entries.Single(e => e.Id == reservationId);
        if (entry.Status == "Reserved")
        {
            entry.Status = settlement.Succeeded ? "Billed" : "Failed";
            entry.Credits = settlement.Credits;
            entry.Usage = settlement.Usage;
            entry.Known = settlement.Usage is not null;
            entry.CostUsd = settlement.CostUsd ?? entry.ReservedUsd;
        }

        return Task.CompletedTask;
    }

    public Task<int> CreditsUsedAsync(Guid userId, DateTimeOffset since, DateTimeOffset liveAfter, CancellationToken cancellationToken) =>
        Task.FromResult(Entries
            .Where(e => e.UserId == userId && e.CreatedAt >= since)
            .Where(e => e.Status == "Billed" || (e.Status == "Reserved" && e.CreatedAt >= liveAfter))
            .Sum(e => e.Credits));

    public Task<AiUsageReport> ReportAsync(DateTimeOffset since, int topUsers, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task AddPurchasedCreditsAsync(Guid userId, int credits, string stripeEventId, CancellationToken cancellationToken)
    {
        if (processedEvents.Add(stripeEventId))
        {
            purchasedBalances[userId] = purchasedBalances.GetValueOrDefault(userId) + credits;
        }
        return Task.CompletedTask;
    }

    public Task<int> PurchasedCreditsBalanceAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(purchasedBalances.GetValueOrDefault(userId));

    internal sealed record Entry(Guid Id, Guid UserId, AiTask Task, DateTimeOffset CreatedAt)
    {
        public string Status { get; set; } = "Reserved";

        public int Credits { get; set; }

        public decimal ReservedUsd { get; set; }

        public decimal CostUsd { get; set; }

        public bool Known { get; set; }

        public ModelUsage? Usage { get; set; }
    }
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
