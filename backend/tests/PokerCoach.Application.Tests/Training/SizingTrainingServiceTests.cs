using Microsoft.Extensions.Time.Testing;
using PokerCoach.Application.Statistics;
using PokerCoach.Application.Training;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Training;

namespace PokerCoach.Application.Tests.Training;

public sealed class SizingTrainingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Played_raises_are_judged_hand_by_hand_and_grouped_by_seat_or_side_and_band()
    {
        var off = Guid.NewGuid();
        var played = new Played(
            // Button opens at 30 BB: 2.25 is in line, 3 is too big, an open-shove too.
            Open(PokerPosition.Button, 30m, 2.25m),
            Open(PokerPosition.Button, 32m, 3m, off),
            Open(PokerPosition.Button, 35m, 35m, allIn: true),
            // 6-max UTG named by distance: 6 players dealt.
            Open(PokerPosition.Utg, 50m, 2.2m),
            // 3-bets: big blind out of position against the cutoff, 9 BB over a 2.2 open (×4.09) at 60 BB: in line;
            // at 22 BB a non-all-in 3-bet is too small.
            ThreeBet(PokerPosition.BigBlind, PokerPosition.Cutoff, 2.2m, 9m, 60m),
            ThreeBet(PokerPosition.BigBlind, PokerPosition.Button, 2m, 6m, 22m));

        var result = await Service(played: played).PlayedAsync(Guid.NewGuid(), TableFormat.SixMax, null, Ct);

        var button = result.Groups.Single(g => g.Kind == SizingKind.Open && g.Seat == PokerPosition.Button);
        Assert.Equal((StackBand.Mid, 3, 1, 0, 2, 1), (button.Band, button.Hands, button.InLine, button.TooSmall, button.TooBig, button.AllIns));
        Assert.Equal(2.63m, button.TypicalSize);
        Assert.Equal(RaiseSize.Of(2.2m), button.Reference);
        Assert.Contains(off, button.OffHands);

        Assert.Equal(StackBand.Deep, result.Groups.Single(g => g.Seat == PokerPosition.Utg).Band);

        var outOfPosition = result.Groups.Where(g => g.Kind == SizingKind.ThreeBet && g.InPosition == false).ToList();
        Assert.Equal(2, outOfPosition.Count);
        Assert.Equal((1, 4.09m), (outOfPosition.Single(g => g.Band == StackBand.Deep).InLine, outOfPosition.Single(g => g.Band == StackBand.Deep).TypicalSize));
        var shortStack = outOfPosition.Single(g => g.Band == StackBand.Short);
        Assert.Equal((1, RaiseSize.AllIn), (shortStack.TooSmall, shortStack.Reference));
    }

    [Fact]
    public async Task An_answer_is_graded_against_the_interval_and_a_size_not_offered_is_refused()
    {
        var store = new Attempts();
        var service = Service(store);
        var item = new SizingItem(TableFormat.SixMax, StackBand.Deep, PokerPosition.BigBlind, 60m, PokerPosition.Cutoff, 2.2m);

        var right = await service.AnswerAsync(Guid.NewGuid(), item, RaiseSize.Of(4.5m), Ct);
        Assert.Equal((true, SizingVerdict.InLine, false, 8.8m), (right.Correct, right.Verdict, right.InPosition, right.Advice.BestRaiseToBigBlinds));

        var wrong = await service.AnswerAsync(Guid.NewGuid(), item, RaiseSize.Of(2.5m), Ct);
        Assert.Equal((false, SizingVerdict.TooSmall), (wrong.Correct, wrong.Verdict));
        Assert.Equal(2, store.Recorded.Count);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AnswerAsync(Guid.NewGuid(), item, RaiseSize.Of(2.2m), Ct));
    }

    [Fact]
    public async Task A_missed_spot_comes_back_until_answered_right()
    {
        var missed = new SizingItem(TableFormat.SixMax, StackBand.Mid, PokerPosition.Button, 30m);
        var store = new Attempts { Recorded = { new SizingAttempt(missed, RaiseSize.Of(3m), false, Now) } };
        var service = Service(store);

        Assert.Equal(1, (await service.ProgressAsync(Guid.NewGuid(), TableFormat.SixMax, Ct)).DueReviews);
        var reviews = 0;
        for (var i = 0; i < 40; i++)
        {
            var next = await service.NextAsync(Guid.NewGuid(), TableFormat.SixMax, StackBand.Mid, null, StakesLevel.Low, Ct);
            reviews += next.Review ? 1 : 0;
            Assert.True(!next.Review || next.Spot.Item == missed);
        }

        Assert.InRange(reviews, 1, 39);
    }

    private static SizingTrainingService Service(Attempts? store = null, Played? played = null) =>
        new(store ?? new Attempts(), played ?? new Played(), new OpeningDrillTests.NoStats(), new FakeTimeProvider(Now), new Random(5));

    private static PlayedRaiseRow Open(PokerPosition seat, decimal stack, decimal openTo, Guid? id = null, bool allIn = false) =>
        new(id ?? Guid.NewGuid(), Now, seat, 6, stack, openTo, allIn, null, null, null, null, false);

    private static PlayedRaiseRow ThreeBet(PokerPosition seat, PokerPosition opener, decimal faced, decimal threeBetTo, decimal effective) =>
        new(Guid.NewGuid(), Now, seat, 6, effective, null, false, opener, faced, effective, threeBetTo, false);

    private sealed class Attempts : ISizingTrainingStore
    {
        public List<SizingAttempt> Recorded { get; } = [];

        public Task RecordAsync(Guid userId, SizingAttempt attempt, int referenceVersion, CancellationToken cancellationToken)
        {
            Recorded.Insert(0, attempt);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SizingAttempt>> RecentAsync(Guid userId, TableFormat format, int count, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SizingAttempt>>(Recorded.Take(count).ToList());

        public Task<IReadOnlySet<Guid>> RealHandOutcomesAsync(Guid userId, TableFormat format, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(
                new HashSet<Guid>(Recorded.Where(a => a.SourceHandId != null && a.Correct).Select(a => a.SourceHandId!.Value)));
    }

    private sealed class Played(params PlayedRaiseRow[] rows) : IPlayedSizingStore
    {
        public Task<IReadOnlyList<PlayedRaiseRow>> ListRaisesAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlayedRaiseRow>>(rows);
    }
}
