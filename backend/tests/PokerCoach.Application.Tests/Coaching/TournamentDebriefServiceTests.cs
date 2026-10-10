using Microsoft.Extensions.Time.Testing;
using PokerCoach.Application.Coaching;
using PokerCoach.Application.Leaks;
using PokerCoach.Application.Tournaments;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Application.Tests.Coaching;

public sealed class TournamentDebriefServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TournamentId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
    private readonly ScriptedModel model = new();
    private readonly LedgerStore ledger = new();
    private readonly InMemoryReportStore reports = new();
    private readonly List<TournamentHandRow> hands = [];
    private TournamentFacts? tournament = Tournament();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Writes_the_debrief_once_and_reuses_it_while_the_tournament_is_unchanged()
    {
        PlayHands(30, doubleUpAt: 10, bustAt: 30);

        var first = await Service().GenerateAsync(UserId, TournamentId, "fr", Ct);
        var second = await Service().GenerateAsync(UserId, TournamentId, "fr", Ct);

        Assert.NotNull(first.View);
        Assert.False(second.View!.Stale);
        Assert.Equal(1, model.Calls);
        Assert.Equal(TournamentDebriefService.Kind, Assert.Single(ledger.Usage).Purpose);
        var facts = model.LastDebrief!.Facts;
        Assert.DoesNotContain("CASSIOPEIA", facts, StringComparison.Ordinal);
        Assert.Contains("Played 30 hands", facts, StringComparison.Ordinal);
        Assert.Contains("M1: level 1", facts, StringComparison.Ordinal);
        Assert.Equal(["M1", "M2"], first.View!.Report.Moments.Select(m => m.Ref));
    }

    [Fact]
    public async Task New_hands_make_the_stored_debrief_stale_until_written_again()
    {
        PlayHands(30, doubleUpAt: 10, bustAt: null);
        await Service().GenerateAsync(UserId, TournamentId, "fr", Ct);

        PlayHands(5, doubleUpAt: null, bustAt: null);
        var view = await Service().GetAsync(UserId, TournamentId, "fr", Ct);

        Assert.True(view!.Stale);
        Assert.Null(await Service().GetAsync(UserId, TournamentId, "en", Ct));
    }

    [Fact]
    public async Task Refuses_unknown_tournaments_pending_facts_and_short_runs_without_paying()
    {
        PlayHands(10, doubleUpAt: null, bustAt: null);
        Assert.Equal(CoachingFailure.NotEnoughData, (await Service().GenerateAsync(UserId, TournamentId, "fr", Ct)).Failure);

        PlayHands(20, doubleUpAt: null, bustAt: null);
        hands.Add(hands[^1] with { HandId = Guid.NewGuid(), Facts = null });
        Assert.Equal(CoachingFailure.HandsPending, (await Service().GenerateAsync(UserId, TournamentId, "fr", Ct)).Failure);

        tournament = null;
        Assert.Equal(CoachingFailure.NotFound, (await Service().GenerateAsync(UserId, TournamentId, "fr", Ct)).Failure);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task Daily_limit_applies_to_new_debriefs()
    {
        PlayHands(30, doubleUpAt: 10, bustAt: null);
        ledger.CallsToday = new CoachingOptions().DailyDebriefsPerUser;

        Assert.Equal(CoachingFailure.DailyLimitReached, (await Service().GenerateAsync(UserId, TournamentId, "fr", Ct)).Failure);
    }

    [Fact]
    public void Only_computed_equity_lets_the_coach_blame_the_cards()
    {
        DebriefMoment Moment(string reference, decimal net, decimal? equity) =>
            new(reference, Guid.NewGuid(), 3, PokerPosition.Button, "AhAd", 20m, net, net / 20m, KeyMomentStage.Showdown, equity);
        var moments = new[] { Moment("M1", -20m, 0.81m), Moment("M2", -20m, null), Moment("M3", 20m, 0.81m) };
        var debrief = new TournamentDebrief(
            "h",
            "s",
            [
                new MomentNote("M3", MomentVerdict.Variance, "won as favourite"),
                new MomentNote("M1", MomentVerdict.Variance, "aces cracked"),
                new MomentNote("M2", MomentVerdict.Variance, "no equity known"),
                new MomentNote("M9", MomentVerdict.Mistake, "not a moment we gave"),
                new MomentNote("M1", MomentVerdict.Mistake, "duplicate"),
            ],
            [],
            []);

        var checkedDebrief = TournamentDebriefService.Checked(debrief, moments);

        Assert.Equal(
            [("M1", MomentVerdict.Variance), ("M2", MomentVerdict.Standard), ("M3", MomentVerdict.Standard)],
            checkedDebrief.Moments.Select(m => (m.Ref, m.Verdict)));
    }

    private TournamentDebriefService Service() =>
        new(new TournamentDetailService(new Store(this)), new LeakService(new NoLeakStats()), reports, ledger, model, new CoachingOptions(), time);

    /// <summary>Hands at 100 BB; a double-up and a bust where asked (1-based indexes among the new hands).</summary>
    private void PlayHands(int count, int? doubleUpAt, int? bustAt)
    {
        for (var i = 1; i <= count; i++)
        {
            var net = i == doubleUpAt ? 20_000L : i == bustAt ? -20_000L : 0L;
            var facts = new HeroHandFacts(
                PokerPosition.Button, 6, 100m, true, net != 0, net != 0, false, false, false, false, false, false, false, false, false,
                net != 0, false, false, net != 0, net > 0, net, net / 200m);
            hands.Add(new TournamentHandRow(Guid.NewGuid(), Start.AddMinutes(hands.Count), 1, 200, 20_000, "AhKd", facts));
        }
    }

    private static TournamentFacts Tournament() =>
        new(TournamentId, "CASSIOPEIA", Start, "EUR", 4m, null, null, 1m, 120, 30, [new EntryOutcome(108, null, null)]);

    private sealed class Store(TournamentDebriefServiceTests test) : ITournamentDetailStore
    {
        public Task<TournamentFacts?> FindAsync(Guid userId, Guid tournamentId, CancellationToken cancellationToken) =>
            Task.FromResult(test.tournament);

        public Task<IReadOnlyList<TournamentHandRow>> GetHandsAsync(Guid tournamentId, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TournamentHandRow>>(test.hands.ToList());
    }
}
