using Microsoft.Extensions.Time.Testing;
using PokerCoach.Application.Leaks;
using PokerCoach.Application.Training;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Training;

namespace PokerCoach.Application.Tests.Training;

public sealed class DefenceDrillTests
{
    [Fact]
    public void The_answer_key_is_the_equilibrium_call_range()
    {
        Assert.Equal(DrillAnswer.Call, OpeningDrill.Expected(Item("AA", PokerPosition.Utg, 14)));
        Assert.Equal(DrillAnswer.Fold, OpeningDrill.Expected(Item("72o", PokerPosition.Utg, 14)));
    }

    [Fact]
    public void The_big_blind_calls_wider_against_a_late_shove_than_an_early_one()
    {
        var chart = PushFoldNash.Solve(TableFormat.SixMax, 10);

        var againstButton = DefenceDrill.Range(Item("AA", PokerPosition.Button, 10));
        var againstUtg = DefenceDrill.Range(Item("AA", PokerPosition.Utg, 10));

        Assert.Equal(chart.Call(PokerPosition.BigBlind, PokerPosition.Button), againstButton);
        Assert.True(ReferenceOpeningRanges.ComboShare(againstButton) > ReferenceOpeningRanges.ComboShare(againstUtg));
    }

    [Fact]
    public void Dealt_spots_are_the_big_blind_against_a_chosen_shover()
    {
        var random = new Random(7);
        for (var i = 0; i < 200; i++)
        {
            var spot = DefenceDrill.Deal(random, TableFormat.SixMax, [PokerPosition.Button, PokerPosition.SmallBlind]);

            Assert.Equal(PokerPosition.BigBlind, spot.Item.Position);
            Assert.Contains(spot.Item.Shover!.Value, new[] { PokerPosition.Button, PokerPosition.SmallBlind });
            Assert.Equal(StackBand.Push, spot.Item.Band);
            Assert.InRange(spot.Item.PushStack!.Value, OpeningDrill.PushStackMin, OpeningDrill.PushStackMax);
            Assert.Equal(spot.Item.Hand, HandClass.Of(spot.First, spot.Second));
        }
    }

    [Fact]
    public void The_big_blind_never_shoves_into_itself() =>
        Assert.DoesNotContain(PokerPosition.BigBlind, DefenceDrill.Shovers(TableFormat.FullRing));

    [Fact]
    public async Task Defence_attempts_are_kept_apart_from_opening_ones()
    {
        var store = new OpeningDrillTests.MemoryStore();
        var service = new OpeningTrainingService(store, new LeakService(new OpeningDrillTests.NoStats()), new FakeTimeProvider(), new Random(3));
        var userId = Guid.NewGuid();

        var spot = await service.NextDefenceAsync(userId, TableFormat.SixMax, [PokerPosition.Button], TestContext.Current.CancellationToken);
        var result = await service.AnswerAsync(userId, Item("72o", PokerPosition.Button, 8), DrillAnswer.Call, TestContext.Current.CancellationToken);
        await service.AnswerAsync(userId, new DrillItem(TableFormat.SixMax, StackBand.Mid, PokerPosition.Utg, RangeNotation.Parse("AA").Single()), DrillAnswer.Raise, TestContext.Current.CancellationToken);
        var defence = await service.ProgressAsync(userId, TableFormat.SixMax, DrillMode.Defence, TestContext.Current.CancellationToken);
        var opening = await service.ProgressAsync(userId, TableFormat.SixMax, DrillMode.Open, TestContext.Current.CancellationToken);

        Assert.Equal(PokerPosition.Button, spot.Spot.Item.Shover);
        Assert.False(result.Correct);
        Assert.Null(result.ReferenceNotation);
        Assert.Equal(1, defence.Attempts);
        Assert.Equal(1, defence.BySeat.Single(s => s.Position == PokerPosition.Button).Attempts);
        Assert.Equal(1, opening.Attempts);
        Assert.Equal(1, opening.Correct);
    }

    private static DrillItem Item(string hand, PokerPosition shover, int stack) =>
        new(TableFormat.SixMax, StackBand.Push, PokerPosition.BigBlind, RangeNotation.Parse(hand).Single(), stack, shover);
}
