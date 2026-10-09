using PokerCoach.Application.Ranges;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Application.Tests.Ranges;

public sealed class RangeServiceTests
{
    [Fact]
    public void Holdings_are_summed_per_hand_and_seat_and_compared_with_the_reference()
    {
        var positions = RangeService.Build(
            TableFormat.FullRing,
            StackBand.Deep,
            [
                new OpeningHoldingCount(PokerPosition.Button, 9, 6, "AhKd", 3, 3, 0),
                new OpeningHoldingCount(PokerPosition.Button, 9, 9, "KsAc", 2, 1, 1),
                new OpeningHoldingCount(PokerPosition.Button, 9, 6, "7h2c", 4, 0, 0),
                // UTG with six dealt at a 9-max table is the lojack: counted there.
                new OpeningHoldingCount(PokerPosition.Utg, 9, 6, "AsAd", 1, 1, 0),
                // Unreadable: left out, not guessed.
                new OpeningHoldingCount(PokerPosition.Button, 9, 6, "??", 5, 5, 0),
                // Another format: not in this view.
                new OpeningHoldingCount(PokerPosition.Button, 6, 6, "AhKd", 7, 7, 0),
            ]);

        Assert.Equal(ReferenceOpeningRanges.Positions(TableFormat.FullRing), positions.Select(p => p.Position));

        var button = positions.Single(p => p.Position == PokerPosition.Button);
        Assert.Equal(169, button.Cells.Count);
        var ako = button.Cells.Single(c => c.Hand.ToString() == "AKo");
        Assert.Equal((5, 4, 1, true), (ako.Dealt, ako.Opens, ako.Limps, ako.InReference));
        Assert.False(button.Cells.Single(c => c.Hand.ToString() == "72o").InReference);
        Assert.Equal(9, button.Dealt);
        Assert.Equal(0.4444m, button.OpenRate.Rate);
        Assert.NotNull(button.ReferenceRate);
        Assert.NotNull(button.ReferenceNotation);

        Assert.Equal(1, positions.Single(p => p.Position == PokerPosition.Lojack).Dealt);
        Assert.Equal(0, positions.Single(p => p.Position == PokerPosition.Utg).Dealt);
    }
}

public sealed class SixMaxRangeTests
{
    [Fact]
    public void A_six_max_utg_keeps_its_name_and_opens_like_a_full_ring_lojack()
    {
        var positions = RangeService.Build(
            TableFormat.SixMax,
            StackBand.Mid,
            [new OpeningHoldingCount(PokerPosition.Utg, 6, 6, "AsAd", 2, 2, 0)]);

        Assert.Equal(
            new[] { PokerPosition.Utg, PokerPosition.Hijack, PokerPosition.Cutoff, PokerPosition.Button, PokerPosition.SmallBlind },
            positions.Select(p => p.Position));
        var utg = positions[0];
        Assert.Equal(2, utg.Dealt);
        Assert.Equal(
            ReferenceOpeningRanges.NotationFor(StackBand.Mid, TableFormat.FullRing, PokerPosition.Lojack),
            utg.ReferenceNotation);
        Assert.Equal(PokerPosition.Lojack, utg.ReferenceRate!.Position);
    }
}
