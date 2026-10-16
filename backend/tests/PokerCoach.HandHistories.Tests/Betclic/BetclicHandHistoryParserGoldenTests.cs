using PokerCoach.Domain.Poker;
using PokerCoach.HandHistories.Betclic;

namespace PokerCoach.HandHistories.Tests.Betclic;

public sealed class BetclicHandHistoryParserGoldenTests
{
    private readonly BetclicHandHistoryParser parser = new();

    [Fact]
    public void Every_hand_of_the_golden_file_is_accepted()
    {
        var result = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicCash6Max));

        Assert.Empty(result.Errors);
        Assert.Equal(8, result.Hands.Count);
    }

    [Fact]
    public void Every_accepted_hand_balances_and_identifies_the_hero()
    {
        var hands = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicCash6Max)).Hands;

        Assert.All(hands, hand =>
        {
            Assert.True(hand.Actions.Sum(a => a.Amount) >= hand.TotalPot);
            Assert.Equal(hand.TotalPot, hand.Collections.Sum(c => c.Amount) + (hand.Rake ?? 0));
            Assert.Equal("Hero", hand.HeroName);
            Assert.Equal(2, hand.HeroCards.Count);
            Assert.Equal(GameType.CashGame, hand.GameType);
            Assert.Null(hand.TournamentName);
            Assert.Null(hand.BuyIn);
            Assert.Equal(0, hand.Level);
            Assert.NotNull(hand.Stakes);
            Assert.All(hand.Actions, action => Assert.Contains(hand.Seats, seat => seat.PlayerName == action.PlayerName));
        });
    }

    [Fact]
    public void Reads_every_field_of_a_simple_hand()
    {
        var hand = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicCash6Max)).Hands[0];

        Assert.Equal(PokerRoom.Betclic, hand.Room);
        Assert.Equal(GameType.CashGame, hand.GameType);
        Assert.Equal("01M4VENZVZXSTKSZR9K4G364P1", hand.ExternalHandId);
        Assert.Null(hand.TournamentName);
        Assert.Null(hand.ExternalTournamentId);
        Assert.Null(hand.BuyIn);
        Assert.Equal(6, hand.MaxSeats);
        Assert.True(hand.IsRealMoney);
        Assert.Equal(0, hand.Level);
        Assert.Null(hand.Ante);
        Assert.Equal(25, hand.SmallBlind);
        Assert.Equal(50, hand.BigBlind);
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 20, 18, 52, TimeSpan.Zero), hand.StartedAt);
        Assert.Equal(5, hand.ButtonSeat);
        Assert.Equal(1, hand.LineNumber);
        Assert.Equal("0.25/0.5", hand.Stakes);
        Assert.Equal(0, hand.Rake);

        Assert.Equal(6, hand.Seats.Count);
        Assert.Equal(new ParsedSeat(1, "Vilain1", 5812, null), hand.Seats[0]);
        Assert.Equal(new ParsedSeat(2, "Hero", 8321, null), hand.Seats[1]);

        Assert.Equal("Hero", hand.HeroName);
        Assert.Equal([new Card(Rank.Six, Suit.Clubs), new Card(Rank.Jack, Suit.Hearts)], hand.HeroCards);

        Assert.Equal(75, hand.TotalPot);
        Assert.Empty(hand.Board);
        Assert.Empty(hand.ShownCards);
    }

    [Fact]
    public void Parses_raise_call_flop_and_rake()
    {
        var hand = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicCash6Max)).Hands[1];

        Assert.Equal(30, hand.Rake);
        Assert.Equal(1100, hand.TotalPot);
        Assert.Equal(3, hand.Board.Count);
        Assert.Equal(new Card(Rank.Four, Suit.Spades), hand.Board[0]);

        var raises = hand.Actions.Where(a => a.Type == ParsedActionType.Raise).ToList();
        Assert.Single(raises);
        Assert.Equal(250, raises[0].RaiseTo);
    }

    [Fact]
    public void Parses_five_max_table_with_seat_gap()
    {
        var hand = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicCash6Max)).Hands[3];

        Assert.Equal(5, hand.Seats.Count);
        Assert.DoesNotContain(hand.Seats, s => s.SeatNumber == 6);
    }

    [Fact]
    public void Parses_showdown_with_shown_cards()
    {
        var hand = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicCash6Max)).Hands[4];

        Assert.Equal(2, hand.ShownCards.Count);
        Assert.Equal("Vilain8", hand.ShownCards[0].PlayerName);
        Assert.Equal("Two Pair", hand.ShownCards[0].HandDescription);
        Assert.Equal([new Card(Rank.Five, Suit.Diamonds), new Card(Rank.Five, Suit.Spades)], hand.ShownCards[0].Cards);
    }

    [Fact]
    public void Parses_all_in_with_hero_winning()
    {
        var hand = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicCash6Max)).Hands[5];

        var allIns = hand.Actions.Where(a => a.IsAllIn).ToList();
        Assert.Single(allIns);
        Assert.Equal("Vilain20", allIns[0].PlayerName);

        Assert.Single(hand.Collections);
        Assert.Equal("Hero", hand.Collections[0].PlayerName);
        Assert.Equal(10485, hand.Collections[0].Amount);
        Assert.Equal(300, hand.Rake);
    }

    [Fact]
    public void Parses_side_pot()
    {
        var hand = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicCash6Max)).Hands[6];

        Assert.Equal(2, hand.Collections.Count);
        Assert.Equal(PotKind.Main, hand.Collections[0].Kind);
        Assert.Equal("Vilain12", hand.Collections[0].PlayerName);
        Assert.Equal(11187, hand.Collections[0].Amount);
        Assert.Equal(PotKind.Side, hand.Collections[1].Kind);
        Assert.Equal(1, hand.Collections[1].SidePotNumber);
        Assert.Equal(1208, hand.Collections[1].Amount);

        Assert.Equal(3, hand.ShownCards.Count);
        Assert.Equal("Straight Flush", hand.ShownCards[1].HandDescription);
    }

    [Fact]
    public void Combined_btn_hero_tag_is_parsed()
    {
        var hand = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicCash6Max)).Hands[2];

        Assert.Equal("Hero", hand.HeroName);
        Assert.Equal(2, hand.ButtonSeat);
    }
}
