using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Training;

namespace PokerCoach.Application.Tests.Training;

public sealed class ReferenceSizingsTests
{
    [Theory]
    [InlineData(PokerPosition.Button, 12, null)]
    [InlineData(PokerPosition.Button, 20, 2.0)]
    [InlineData(PokerPosition.Utg, 30, 2.2)]
    [InlineData(PokerPosition.Cutoff, 60, 2.2)]
    [InlineData(PokerPosition.SmallBlind, 20, 2.5)]
    [InlineData(PokerPosition.SmallBlind, 50, 3.0)]
    public void Opens_shove_in_push_fold_and_otherwise_raise_small_the_small_blind_bigger(PokerPosition seat, int stack, double? best)
    {
        var advice = ReferenceSizings.Open(seat, stack);

        Assert.Equal(best is { } b ? RaiseSize.Of((decimal)b) : RaiseSize.AllIn, advice.Best);
    }

    [Fact]
    public void A_3bet_is_3x_in_position_4x_out_of_position_and_a_shove_once_it_commits()
    {
        var inPosition = ReferenceSizings.ThreeBet(PokerPosition.Button, PokerPosition.Cutoff, 2.2m, 60m);
        Assert.Equal((RaiseSize.Of(3m), 6.6m), (inPosition.Best, inPosition.BestRaiseToBigBlinds));

        var outOfPosition = ReferenceSizings.ThreeBet(PokerPosition.BigBlind, PokerPosition.Cutoff, 2.2m, 60m);
        Assert.Equal((RaiseSize.Of(4m), 8.8m), (outOfPosition.Best, outOfPosition.BestRaiseToBigBlinds));

        // The big blind plays the small blind in position.
        Assert.Equal(RaiseSize.Of(3m), ReferenceSizings.ThreeBet(PokerPosition.BigBlind, PokerPosition.SmallBlind, 3m, 60m).Best);

        // 8.8 BB out of 26 is more than a third: shove. Below 25 BB, always.
        Assert.True(ReferenceSizings.ThreeBet(PokerPosition.BigBlind, PokerPosition.Cutoff, 2.2m, 26m).Best.IsAllIn);
        Assert.True(ReferenceSizings.ThreeBet(PokerPosition.Button, PokerPosition.Cutoff, 2m, 22m).Best.IsAllIn);
        Assert.False(ReferenceSizings.ThreeBet(PokerPosition.Button, PokerPosition.Cutoff, 2m, 26m).Best.IsAllIn);
    }

    [Fact]
    public void Played_sizes_are_judged_against_the_interval_rounded_to_a_tenth()
    {
        var open = ReferenceSizings.Open(PokerPosition.Cutoff, 30m);
        Assert.Equal(SizingVerdict.InLine, open.Judge(RaiseSize.Of(2.25m)));
        Assert.Equal(SizingVerdict.TooBig, open.Judge(RaiseSize.Of(3m)));
        Assert.Equal(SizingVerdict.TooBig, open.Judge(RaiseSize.AllIn));
        Assert.Equal(SizingVerdict.TooSmall, ReferenceSizings.Open(PokerPosition.SmallBlind, 30m).Judge(RaiseSize.Of(2m)));

        var shove = ReferenceSizings.ThreeBet(PokerPosition.SmallBlind, PokerPosition.Button, 2m, 20m);
        Assert.Equal(SizingVerdict.InLine, shove.Judge(RaiseSize.AllIn));
        Assert.Equal(SizingVerdict.TooSmall, shove.Judge(RaiseSize.Of(4m)));
    }
}

public sealed class SizingDrillTests
{
    [Fact]
    public void Every_dealt_spot_has_a_hand_the_reference_plays_and_one_offered_size_is_right()
    {
        var random = new Random(7);
        foreach (var format in Enum.GetValues<TableFormat>())
        {
            foreach (var band in new[] { StackBand.Short, StackBand.Mid, StackBand.Deep })
            {
                for (var i = 0; i < 200; i++)
                {
                    var spot = SizingDrill.Deal(random, format, band, null);
                    var item = spot.Item;
                    Assert.Contains(SizingDrill.Choices(item), c => SizingDrill.IsCorrect(item, c));
                    Assert.Equal(spot.Hand, HandClass.Of(spot.First, spot.Second));

                    if (item.Kind == SizingKind.Open)
                    {
                        Assert.Contains(spot.Hand, ReferenceOpeningRanges.For(band, format, item.Position)!);
                    }
                    else
                    {
                        Assert.Contains((item.Position, item.Opener!.Value), SizingDrill.ThreeBetPairs(format));
                        Assert.Contains(spot.Hand, ReferenceDefenceRanges.For(StackBand.Mid, format, item.Position, item.Opener.Value)!.ThreeBet);
                    }
                }
            }
        }
    }

    [Fact]
    public void A_review_keeps_the_spot_and_push_fold_is_not_drilled()
    {
        var item = new SizingItem(TableFormat.SixMax, StackBand.Mid, PokerPosition.BigBlind, 30m, PokerPosition.Button, 2.2m);

        Assert.Equal(item, SizingDrill.Deal(new Random(1), TableFormat.SixMax, StackBand.Mid, SizingKind.Open, item).Item);
        Assert.Throws<ArgumentOutOfRangeException>(() => SizingDrill.Deal(new Random(1), TableFormat.SixMax, StackBand.Push, null));
        Assert.False(SizingDrill.IsOffered(item, RaiseSize.Of(2.2m)));
        Assert.True(SizingDrill.IsOffered(item, RaiseSize.Of(4m)));
    }
}
