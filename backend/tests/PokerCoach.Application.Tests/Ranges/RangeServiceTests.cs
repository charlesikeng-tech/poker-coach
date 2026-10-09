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
            StackBand.Deep,
            [
                new OpeningHoldingCount(PokerPosition.Button, 6, "AhKd", 3, 3, 0),
                new OpeningHoldingCount(PokerPosition.Button, 9, "KsAc", 2, 1, 1),
                new OpeningHoldingCount(PokerPosition.Button, 6, "7h2c", 4, 0, 0),
                // UTG at a 6-handed table is the lojack: counted there.
                new OpeningHoldingCount(PokerPosition.Utg, 6, "AsAd", 1, 1, 0),
                // Unreadable: left out, not guessed.
                new OpeningHoldingCount(PokerPosition.Button, 6, "??", 5, 5, 0),
            ]);

        Assert.Equal(ReferenceOpeningRanges.Positions, positions.Select(p => p.Position));

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
