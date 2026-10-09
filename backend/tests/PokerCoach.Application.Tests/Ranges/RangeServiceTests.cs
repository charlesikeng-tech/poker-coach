using PokerCoach.Application.Ranges;
using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Application.Tests.Ranges;

public sealed class RangeServiceTests
{
    [Fact]
    public void Holdings_of_one_class_are_summed_and_every_hand_is_listed()
    {
        var positions = RangeService.Build(
        [
            new OpeningHoldingCount(PokerPosition.Button, "AhKd", 3, 3, 0),
            new OpeningHoldingCount(PokerPosition.Button, "KsAc", 2, 1, 1),
            new OpeningHoldingCount(PokerPosition.Button, "7h2c", 4, 0, 0),
            new OpeningHoldingCount(PokerPosition.Utg, "AsAd", 1, 1, 0),
            // Unreadable: left out, not guessed.
            new OpeningHoldingCount(PokerPosition.Utg, "??", 5, 5, 0),
        ]);

        Assert.Equal(new[] { PokerPosition.Utg, PokerPosition.Button }, positions.Select(p => p.Position));

        var button = positions[1];
        Assert.Equal(169, button.Cells.Count);
        var ako = button.Cells.Single(c => c.Hand.ToString() == "AKo");
        Assert.Equal((5, 4, 1), (ako.Dealt, ako.Opens, ako.Limps));
        Assert.Equal(9, button.Dealt);
        Assert.Equal(4, button.Opens);
        Assert.Equal(0.4444m, button.OpenRate.Rate);
        Assert.NotNull(button.Reference);
        Assert.Equal(0, button.Cells.Single(c => c.Hand.ToString() == "AA").Dealt);

        Assert.Equal(1, positions[0].Dealt);
    }
}
