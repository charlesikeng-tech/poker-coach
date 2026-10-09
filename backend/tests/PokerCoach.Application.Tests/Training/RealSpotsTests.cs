using Microsoft.Extensions.Time.Testing;
using PokerCoach.Application.Leaks;
using PokerCoach.Application.Training;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Training;

namespace PokerCoach.Application.Tests.Training;

public sealed class RealSpotsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_folded_ace_king_on_the_button_is_a_missed_open()
    {
        var spot = RealSpots.From(Row("AhKd", PokerPosition.Button, stack: 30m), TableFormat.SixMax)!;

        Assert.Equal(StackBand.Mid, spot.Spot.Item.Band);
        Assert.Equal(PokerPosition.Button, spot.Spot.Item.Position);
        Assert.Equal("AKo", spot.Spot.Item.Hand.ToString());
        Assert.Equal(DrillAnswer.Raise, spot.Expected);
        Assert.Equal(RealAction.Fold, spot.Actual);
        Assert.True(spot.Missed);
    }

    [Fact]
    public void A_limp_is_always_a_miss_and_a_right_fold_is_not()
    {
        Assert.True(RealSpots.From(Row("AhKd", PokerPosition.Cutoff, 30m, limp: true), TableFormat.SixMax)!.Missed);
        Assert.False(RealSpots.From(Row("7h2c", PokerPosition.Utg, 30m), TableFormat.SixMax)!.Missed);
    }

    [Fact]
    public void Below_15_big_blinds_the_spot_is_push_fold_at_the_rounded_stack()
    {
        var spot = RealSpots.From(Row("Ah9c", PokerPosition.Button, 9.6m, rfi: true), TableFormat.SixMax)!;

        Assert.Equal(StackBand.Push, spot.Spot.Item.Band);
        Assert.Equal(10, spot.Spot.Item.PushStack);
        Assert.Equal(9.6m, spot.Spot.StackBigBlinds);
    }

    [Fact]
    public void Seats_without_a_reference_and_unreadable_cards_are_left_out()
    {
        Assert.Null(RealSpots.From(Row("AhKd", PokerPosition.BigBlind, 30m), TableFormat.SixMax));
        Assert.Null(RealSpots.From(Row("AhAh", PokerPosition.Button, 30m), TableFormat.SixMax));
        Assert.Null(RealSpots.From(Row("Ah", PokerPosition.Button, 30m), TableFormat.SixMax));
    }

    [Fact]
    public async Task The_quiz_asks_missed_spots_until_answered_right()
    {
        var missed = Row("AhKd", PokerPosition.Button, 30m);
        var fine = Row("7h2c", PokerPosition.Utg, 30m);
        var store = new OpeningDrillTests.MemoryStore();
        var service = new OpeningTrainingService(store, new LeakService(new OpeningDrillTests.NoStats()), new FakeTimeProvider(Now), new Random(1), new StubRealSpots(missed, fine));
        var userId = Guid.NewGuid();

        var first = (await service.NextRealAsync(userId, TableFormat.SixMax, TestContext.Current.CancellationToken))!;
        await service.AnswerAsync(userId, first.Drill.Spot.Item, DrillAnswer.Fold, TestContext.Current.CancellationToken, first.HandId);
        var again = (await service.NextRealAsync(userId, TableFormat.SixMax, TestContext.Current.CancellationToken))!;
        await service.AnswerAsync(userId, again.Drill.Spot.Item, DrillAnswer.Raise, TestContext.Current.CancellationToken, again.HandId);
        var done = await service.NextRealAsync(userId, TableFormat.SixMax, TestContext.Current.CancellationToken);

        Assert.Equal(missed.HandId, first.HandId);
        Assert.Equal(RealAction.Fold, first.Actual);
        Assert.Equal(1, first.Remaining);
        Assert.False(first.Drill.Review);
        Assert.True(again.Drill.Review);
        Assert.Null(done);
    }

    private static RealOpeningRow Row(string cards, PokerPosition position, decimal stack, bool rfi = false, bool limp = false) =>
        new(Guid.NewGuid(), Now.AddDays(-3), position, 6, stack, cards, rfi, limp);

    private sealed class StubRealSpots(params RealOpeningRow[] rows) : IRealSpotStore
    {
        public Task<IReadOnlyList<RealOpeningRow>> ListOpeningSpotsAsync(Guid userId, TableFormat format, DateTimeOffset since, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RealOpeningRow>>(rows);
    }
}
