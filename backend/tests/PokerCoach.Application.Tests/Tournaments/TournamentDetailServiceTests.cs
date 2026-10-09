using PokerCoach.Application.Tournaments;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Application.Tests.Tournaments;

public sealed class TournamentDetailServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TournamentId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Unknown_or_foreign_tournament_is_null()
    {
        var service = new TournamentDetailService(new StubStore(null, []));

        Assert.Null(await service.GetAsync(UserId, TournamentId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Builds_stack_curve_key_moments_and_stats_from_measured_facts()
    {
        var store = new StubStore(
            Facts(),
            [
                Hand(0, 20_000, 100, Facts(PokerPosition.Button, vpip: true, pfr: true, net: 400)),
                Hand(2, 20_400, 100, Facts(PokerPosition.BigBlind, vpip: false, pfr: false, net: -100)),
                Hand(5, 20_300, 200, Facts(PokerPosition.Cutoff, vpip: true, pfr: true, net: 20_300, showdown: true)),
                // Facts not computed yet: on the curve, not in stats or key moments.
                Hand(7, 40_600, 200, null),
            ]);

        var detail = await new TournamentDetailService(store).GetAsync(UserId, TournamentId, TestContext.Current.CancellationToken);

        Assert.NotNull(detail);
        Assert.Equal(new[] { 200m, 204m, 101.5m, 203m }, detail.Stack.Select(p => p.StackInBigBlinds));
        Assert.Equal(new[] { 1, 2, 3, 4 }, detail.Stack.Select(p => p.Index));
        var moment = Assert.Single(detail.KeyMoments);
        Assert.Equal(3, moment.Moment.Index);
        Assert.Equal(1m, moment.Moment.StackShare);
        Assert.Equal(KeyMomentStage.Showdown, moment.Moment.Stage);
        Assert.Equal(PokerPosition.Cutoff, moment.Position);
        Assert.Equal(3, detail.Stats.Hands);
        Assert.Equal(2, detail.Stats.Vpip.Made);
        Assert.Equal(3, detail.Stats.Vpip.Opportunities);
        Assert.Equal(1, detail.PendingHands);
        Assert.Equal(TimeSpan.FromMinutes(7), detail.HandsDuration);
    }

    private static TournamentHandRow Hand(int minute, long stack, long bigBlind, HeroHandFacts? facts) =>
        new(Guid.NewGuid(), Start.AddMinutes(minute), 1, bigBlind, stack, "AhKd", facts);

    private static HeroHandFacts Facts(PokerPosition position, bool vpip, bool pfr, long net, bool showdown = false) =>
        new(position, 6, 100m, true, vpip, pfr, false, false, false, false, false, false, false, false, false,
            showdown, false, false, showdown, showdown, net, net / 100m);

    private static TournamentFacts Facts() =>
        new(TournamentId, "CASSIOPEIA", Start, "EUR", 4m, null, null, 1m, 120, 4, [new EntryOutcome(3, 50m, null)]);

    private sealed class StubStore(TournamentFacts? facts, IReadOnlyList<TournamentHandRow> hands) : ITournamentDetailStore
    {
        public Task<TournamentFacts?> FindAsync(Guid userId, Guid tournamentId, CancellationToken cancellationToken) =>
            Task.FromResult(facts);

        public Task<IReadOnlyList<TournamentHandRow>> GetHandsAsync(Guid tournamentId, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult(hands);
    }
}
