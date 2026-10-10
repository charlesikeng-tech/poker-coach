using PokerCoach.Application.Tests.Subscriptions;
using Microsoft.Extensions.Time.Testing;
using PokerCoach.Application.Coaching;
using PokerCoach.Application.Leaks;
using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.HandHistories.Winamax;

namespace PokerCoach.Application.Tests.Coaching;

public sealed class LeakCoachServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeStore store = new();
    private readonly FakeModel model = new();
    private readonly CoachingOptions options = new() { MonthlyBudgetUsd = 20m, DailyExplanationsPerUser = 2 };

    // Button opens 20 % (reference 35–52 %): a confirmed "too low" leak.
    private static readonly HeroStatCounts Button = HeroStatCounts.Zero with { Hands = 300, RfiOpportunities = 200, Rfi = 40 };

    [Fact]
    public async Task Explains_a_current_leak_and_reuses_the_explanation()
    {
        var first = await ExplainButtonAsync();
        var second = await ExplainButtonAsync();

        Assert.NotNull(first.Explanation);
        Assert.Equal(first.Explanation, second.Explanation);
        Assert.Equal(1, model.Calls);
        Assert.Single(store.Usage);
        Assert.Contains("Rfi from BTN", model.LastPrompt!.Facts, StringComparison.Ordinal);
        Assert.Contains("20 %", model.LastPrompt.Facts, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cited_hands_must_be_hands_we_gave()
    {
        store.Examples.Add(Example());
        model.Cite = ["H1", "H9"];

        var outcome = await ExplainButtonAsync();

        var hand = Assert.Single(outcome.Explanation!.Hands);
        Assert.Equal("H1", hand.Ref);
        Assert.Equal("H1", Assert.Single(outcome.Explanation.Explanation.Hands).Ref);
    }

    [Fact]
    public async Task A_statistic_that_is_not_a_leak_is_refused()
    {
        var outcome = await Service().ExplainAsync(UserId, LeakStat.Rfi, PokerPosition.Utg, LeakDirection.TooHigh, null, null, "fr", Ct);

        Assert.Equal(CoachingFailure.NotALeak, outcome.Failure);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task Without_a_key_the_coach_is_unavailable()
    {
        model.Available = false;

        Assert.Equal(CoachingFailure.Unavailable, (await ExplainButtonAsync()).Failure);
    }

    [Fact]
    public async Task Daily_limit_and_monthly_budget_are_enforced()
    {
        store.CallsToday = 2;
        Assert.Equal(CoachingFailure.DailyLimitReached, (await ExplainButtonAsync()).Failure);

        store.CallsToday = 0;
        store.Spend = 20m;
        Assert.Equal(CoachingFailure.BudgetExhausted, (await ExplainButtonAsync()).Failure);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task A_model_failure_is_reported_not_thrown()
    {
        model.Fail = true;

        Assert.Equal(CoachingFailure.ModelFailed, (await ExplainButtonAsync()).Failure);
    }

    [Fact]
    public void Cost_follows_token_prices()
    {
        var cost = options.CostOf(new ModelUsage("m", 6_000, 1_500, 0, 2_000));

        Assert.Equal((6_000 * 2m + 1_500 * 10m + 2_000 * 0.10m) / 1_000_000m, cost);
    }

    [Fact]
    public void The_hand_story_names_nobody()
    {
        var hand = new WinamaxHandHistoryParser().Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", "Winamax", "20260905_CASSIOPEIA_1161415333__real_holdem_no-limit.txt"))).Hands[1];
        var input = HandForAnalysisMapper.From(hand)!;

        var story = HandNarrator.Tell(new ExampleHand(Guid.NewGuid(), hand.StartedAt, hand.Level, hand.Ante, input, hand.Board, "4cKs"));

        Assert.Contains("Hero (HJ)", story, StringComparison.Ordinal);
        Assert.Contains("Hero holds 4cKs", story, StringComparison.Ordinal);
        Assert.DoesNotContain("Villain0", story, StringComparison.Ordinal);
        Assert.DoesNotContain("CASSIOPEIA", story, StringComparison.Ordinal);
        Assert.Contains("BTN raises", story, StringComparison.Ordinal);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<CoachingOutcome> ExplainButtonAsync() =>
        Service().ExplainAsync(UserId, LeakStat.Rfi, PokerPosition.Button, LeakDirection.TooLow, null, null, "fr", Ct);

    private LeakCoachService Service() =>
        new(new LeakService(new StatsStub()), store, model, options, BillingFakes.Unbilled(time), time);

    private static ExampleHand Example()
    {
        var hand = new HandForAnalysis(
            6,
            200,
            "Hero",
            new[] { "P1", "P2", "P3", "P4", "P5", "Hero" }.Select((p, i) => new SeatState(i + 1, p, 8_000)).ToList(),
            [
                new HandAction(Street.Preflop, "P1", ActionKind.PostSmallBlind, 100, false),
                new HandAction(Street.Preflop, "P2", ActionKind.PostBigBlind, 200, false),
                new HandAction(Street.Preflop, "P3", ActionKind.Fold, 0, false),
                new HandAction(Street.Preflop, "P4", ActionKind.Fold, 0, false),
                new HandAction(Street.Preflop, "P5", ActionKind.Fold, 0, false),
                new HandAction(Street.Preflop, "Hero", ActionKind.Fold, 0, false),
                new HandAction(Street.Preflop, "P1", ActionKind.Fold, 0, false),
            ],
            new Dictionary<string, long> { ["P2"] = 300 });
        return new ExampleHand(Guid.NewGuid(), DateTimeOffset.UnixEpoch, 5, null, hand, [], "Kd9s");
    }

    private sealed class StatsStub : IStatisticsReadStore
    {
        public Task<IReadOnlyList<(PokerPosition? Position, HeroStatCounts Counts)>> CountByPositionAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<(PokerPosition?, HeroStatCounts)>>([(PokerPosition.Button, Button)]);

        public Task<StatisticsSample> CountTournamentsAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult(new StatisticsSample(10, 5));

        public Task<FormatCounts> CountByFormatAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult(new FormatCounts(500, 0));

        public Task<IReadOnlyList<(DateOnly Month, HeroStatCounts Counts)>> CountByMonthAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>

            Task.FromResult<IReadOnlyList<(DateOnly, HeroStatCounts)>>([]);


        public Task<int> CountPendingAsync(Guid userId, int factsVersion, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class FakeModel : ICoachingModel
    {
        public bool Available { get; set; } = true;

        public bool Fail { get; set; }

        public int Calls { get; private set; }

        public string[] Cite { get; set; } = [];

        public ExplanationPrompt? LastPrompt { get; private set; }

        public bool IsAvailable => Available;

        public Task<ModelExplanation> ExplainAsync(ExplanationPrompt prompt, CancellationToken cancellationToken)
        {
            Calls++;
            LastPrompt = prompt;
            if (Fail)
            {
                throw new CoachingModelException("down");
            }

            return Task.FromResult(new ModelExplanation(
                new LeakExplanation("summary", "why", ["act"], Cite.Select(c => new HandNote(c, "note " + c)).ToList()),
                new ModelUsage("test-model", 6_000, 1_000, 0, 0)));
        }

        public Task<ModelAnswer<TournamentDebrief>> DebriefTournamentAsync(DebriefPrompt prompt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ModelAnswer<WeekReview>> ReviewWeekAsync(WeekReviewPrompt prompt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeStore : ICoachingStore
    {
        private readonly Dictionary<string, StoredExplanation> explanations = [];

        public List<ExampleHand> Examples { get; } = [];

        public List<ModelUsage> Usage { get; } = [];

        public int CallsToday { get; set; }

        public decimal Spend { get; set; }

        public Task<StoredExplanation?> FindExplanationAsync(Guid userId, string fingerprint, CancellationToken cancellationToken) =>
            Task.FromResult(explanations.GetValueOrDefault(fingerprint));

        public Task SaveExplanationAsync(Guid userId, string fingerprint, string language, StoredExplanation explanation, CancellationToken cancellationToken)
        {
            explanations[fingerprint] = explanation;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ExampleHand>> FindExampleHandsAsync(Guid userId, LeakSituation situation, int count, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExampleHand>>(Examples);

        public Task RecordUsageAsync(Guid? userId, string purpose, ModelUsage usage, decimal costUsd, CancellationToken cancellationToken)
        {
            Usage.Add(usage);
            return Task.CompletedTask;
        }

        public Task<decimal> SpendSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) => Task.FromResult(Spend);

        public Task<int> CountCallsSinceAsync(Guid userId, string purpose, DateTimeOffset since, CancellationToken cancellationToken) => Task.FromResult(CallsToday);
    }
}
