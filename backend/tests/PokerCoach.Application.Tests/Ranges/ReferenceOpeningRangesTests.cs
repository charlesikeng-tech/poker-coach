using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Application.Tests.Ranges;

public sealed class ReferenceOpeningRangesTests
{
    [Theory]
    [InlineData("77", new[] { "77" })]
    [InlineData("QQ+", new[] { "QQ", "KK", "AA" })]
    [InlineData("99-77", new[] { "77", "88", "99" })]
    [InlineData("KTs+", new[] { "KTs", "KJs", "KQs" })]
    [InlineData("A5s-A3s", new[] { "A3s", "A4s", "A5s" })]
    [InlineData("T9o+", new[] { "T9o" })]
    [InlineData("AKo, AKs", new[] { "AKo", "AKs" })]
    public void Notation_expands_to_its_hands(string notation, string[] expected)
    {
        var hands = RangeNotation.Parse(notation).Select(h => h.ToString()).Order(StringComparer.Ordinal);

        Assert.Equal(expected.Order(StringComparer.Ordinal), hands);
    }

    [Theory]
    [InlineData("AK")]
    [InlineData("AAs")]
    [InlineData("A5s-K2s")]
    [InlineData("A5s-A2o")]
    [InlineData("Z9s")]
    public void Malformed_notation_fails_loudly(string notation)
    {
        Assert.Throws<FormatException>(() => RangeNotation.Parse(notation));
    }

    [Fact]
    public void Every_reference_open_rate_stays_inside_the_leak_reference_rate()
    {
        foreach (var band in Enum.GetValues<StackBand>())
        {
            foreach (var position in ReferenceOpeningRanges.Positions)
            {
                var range = ReferenceOpeningRanges.For(band, position);
                Assert.NotNull(range);
                var share = ReferenceOpeningRanges.ComboShare(range);
                var rate = ReferenceRanges.LowStakesMtt.Single(r => r.Stat == LeakStat.Rfi && r.Position == position);
                Assert.InRange(share, rate.Min, rate.Max);
            }
        }
    }

    [Fact]
    public void Ranges_widen_from_utg_to_the_button_and_with_depth()
    {
        foreach (var band in Enum.GetValues<StackBand>())
        {
            var shares = ReferenceOpeningRanges.Positions
                .Where(p => p != PokerPosition.SmallBlind)
                .Select(p => ReferenceOpeningRanges.ComboShare(ReferenceOpeningRanges.For(band, p)!))
                .ToList();
            Assert.Equal(shares.Order(), shares);
        }

        var button = Enum.GetValues<StackBand>()
            .Select(b => ReferenceOpeningRanges.ComboShare(ReferenceOpeningRanges.For(b, PokerPosition.Button)!))
            .ToList();
        Assert.Equal(button.Order(), button);
    }

    [Theory]
    [InlineData(PokerPosition.Utg, 9, PokerPosition.Utg)]
    [InlineData(PokerPosition.Utg1, 9, PokerPosition.Utg1)]
    [InlineData(PokerPosition.Utg2, 9, PokerPosition.Utg2)]
    [InlineData(PokerPosition.Utg, 8, PokerPosition.Utg1)]
    [InlineData(PokerPosition.Utg1, 8, PokerPosition.Utg2)]
    [InlineData(PokerPosition.Utg, 6, PokerPosition.Lojack)]
    [InlineData(PokerPosition.Utg, 5, PokerPosition.Hijack)]
    [InlineData(PokerPosition.Cutoff, 6, PokerPosition.Cutoff)]
    [InlineData(PokerPosition.SmallBlind, 6, PokerPosition.SmallBlind)]
    public void Seats_are_named_by_distance_to_the_button(PokerPosition position, int players, PokerPosition expected)
    {
        Assert.Equal(expected, OpeningSeat.Canonical(position, players));
    }
}
