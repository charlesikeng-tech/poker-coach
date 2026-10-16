using PokerCoach.Domain.Poker;
using PokerCoach.HandHistories.Pmu;

namespace PokerCoach.HandHistories.Tests.Pmu;

public sealed class PmuHandHistoryParserGoldenTests
{
    private readonly PmuHandHistoryParser parser = new();

    [Fact]
    public void Every_hand_of_the_golden_file_is_accepted()
    {
        var result = parser.Parse(GoldenFiles.ReadPmu(GoldenFiles.PmuCash6Max));

        Assert.Empty(result.Errors);
        Assert.Equal(8, result.Hands.Count);
    }

    [Fact]
    public void Every_accepted_hand_balances_and_identifies_the_hero()
    {
        var hands = parser.Parse(GoldenFiles.ReadPmu(GoldenFiles.PmuCash6Max)).Hands;

        Assert.All(hands, hand =>
        {
            Assert.True(hand.Actions.Sum(a => a.Amount) >= hand.TotalPot);
            Assert.Equal(hand.TotalPot, hand.Collections.Sum(c => c.Amount) + (hand.Rake ?? 0));
            Assert.Equal("Héros", hand.HeroName);
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
        var hand = parser.Parse(GoldenFiles.ReadPmu(GoldenFiles.PmuCash6Max)).Hands[0];

        Assert.Equal(PokerRoom.Pmu, hand.Room);
        Assert.Equal(GameType.CashGame, hand.GameType);
        Assert.Equal("9126398469", hand.ExternalHandId);
        Assert.Null(hand.TournamentName);
        Assert.Null(hand.ExternalTournamentId);
        Assert.Null(hand.BuyIn);
        Assert.Equal(6, hand.MaxSeats);
        Assert.True(hand.IsRealMoney);
        Assert.Equal(0, hand.Level);
        Assert.Null(hand.Ante);
        Assert.Equal(25, hand.SmallBlind);
        Assert.Equal(50, hand.BigBlind);
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 20, 16, 47, TimeSpan.Zero), hand.StartedAt);
        Assert.Equal(8, hand.ButtonSeat);
        Assert.Equal(1, hand.LineNumber);
        Assert.Equal("0.25/0.50", hand.Stakes);
        Assert.Equal(0, hand.Rake);

        Assert.Equal(6, hand.Seats.Count);
        Assert.Equal(new ParsedSeat(1, "Vilain 121", 5000, null), hand.Seats[0]);
        Assert.Equal(new ParsedSeat(3, "Héros", 5847, null), hand.Seats[1]);

        Assert.Equal("Héros", hand.HeroName);
        Assert.Equal([new Card(Rank.Six, Suit.Spades), new Card(Rank.Nine, Suit.Clubs)], hand.HeroCards);

        Assert.Equal(100, hand.TotalPot);
        Assert.Empty(hand.Board);
        Assert.Empty(hand.ShownCards);
    }

    [Fact]
    public void Parses_3bet_flop_and_turn_with_rake()
    {
        var hand = parser.Parse(GoldenFiles.ReadPmu(GoldenFiles.PmuCash6Max)).Hands[1];

        Assert.Equal(91, hand.Rake);
        Assert.Equal(1278 + 91, hand.TotalPot);
        Assert.Equal(4, hand.Board.Count);
        Assert.Equal(new Card(Rank.Two, Suit.Spades), hand.Board[0]);
        Assert.Equal(new Card(Rank.Five, Suit.Diamonds), hand.Board[3]);

        var raises = hand.Actions.Where(a => a.Type == ParsedActionType.Raise).ToList();
        Assert.Equal(2, raises.Count);
        Assert.Equal(125, raises[0].RaiseTo);
        Assert.Equal(375, raises[1].RaiseTo);
    }

    [Fact]
    public void Parses_hero_dealer_and_walk()
    {
        var hand = parser.Parse(GoldenFiles.ReadPmu(GoldenFiles.PmuCash6Max)).Hands[2];

        Assert.Equal(6, hand.ButtonSeat);
        Assert.Equal("Héros", hand.HeroName);
        Assert.Equal(5, hand.Seats.Count);
        Assert.Equal(0, hand.Rake);
        Assert.Equal(50, hand.TotalPot);
        Assert.Empty(hand.Board);
    }

    [Fact]
    public void Parses_hero_wins_without_showdown_and_mucks()
    {
        var hand = parser.Parse(GoldenFiles.ReadPmu(GoldenFiles.PmuCash6Max)).Hands[3];

        Assert.Single(hand.Collections);
        Assert.Equal("Héros", hand.Collections[0].PlayerName);
        Assert.Equal(327, hand.Collections[0].Amount);
        Assert.Equal(23, hand.Rake);
        Assert.Equal(327 + 23, hand.TotalPot);

        Assert.Equal(4, hand.Board.Count);
        Assert.Empty(hand.ShownCards);
    }

    [Fact]
    public void Parses_allin_with_showdown()
    {
        var hand = parser.Parse(GoldenFiles.ReadPmu(GoldenFiles.PmuCash6Max)).Hands[4];

        var allIns = hand.Actions.Where(a => a.IsAllIn).ToList();
        Assert.Single(allIns);
        Assert.Equal("Vilain 133", allIns[0].PlayerName);
        Assert.Equal(ParsedActionType.Call, allIns[0].Type);

        Assert.Equal(2, hand.ShownCards.Count);
        Assert.Equal("Vilain 133", hand.ShownCards[0].PlayerName);
        Assert.Equal("Two Pair, Aces and Tens", hand.ShownCards[0].HandDescription);
        Assert.Equal("Vilain 124", hand.ShownCards[1].PlayerName);
        Assert.Equal("Three of a Kind, Aces", hand.ShownCards[1].HandDescription);

        Assert.Single(hand.Collections);
        Assert.Equal("Vilain 124", hand.Collections[0].PlayerName);
        Assert.Equal(5413, hand.Collections[0].Amount);
        Assert.Equal(300, hand.Rake);
    }

    [Fact]
    public void Parses_raise_then_allin_on_turn()
    {
        var hand = parser.Parse(GoldenFiles.ReadPmu(GoldenFiles.PmuCash6Max)).Hands[5];

        var allIns = hand.Actions.Where(a => a.IsAllIn).ToList();
        Assert.Single(allIns);
        Assert.Equal("Vilain 136", allIns[0].PlayerName);
        Assert.Equal(ParsedActionType.Call, allIns[0].Type);

        Assert.Equal(5, hand.Board.Count);
        Assert.Equal(new Card(Rank.Six, Suit.Hearts), hand.Board[4]);

        Assert.Equal(2, hand.ShownCards.Count);
        Assert.Equal(166, hand.Rake);
    }

    [Fact]
    public void Parses_4bet_allin_preflop_with_runout()
    {
        var hand = parser.Parse(GoldenFiles.ReadPmu(GoldenFiles.PmuCash6Max)).Hands[6];

        var raises = hand.Actions.Where(a => a.Type == ParsedActionType.Raise).ToList();
        Assert.Equal(3, raises.Count);
        Assert.Equal(125, raises[0].RaiseTo);
        Assert.Equal(200, raises[1].RaiseTo);
        Assert.Equal(5936, raises[2].RaiseTo);

        var allIns = hand.Actions.Where(a => a.IsAllIn).ToList();
        Assert.Equal(2, allIns.Count);
        // Vilain 129 raises to their entire stack (59.36 = starting stack).
        Assert.Equal("Vilain 129", allIns[0].PlayerName);
        Assert.Equal(ParsedActionType.Raise, allIns[0].Type);
        // Vilain 137 explicitly goes all-in (Allin keyword).
        Assert.Equal("Vilain 137", allIns[1].PlayerName);
        Assert.Equal(ParsedActionType.Call, allIns[1].Type);

        Assert.Equal(5, hand.Board.Count);
        Assert.Equal(2, hand.ShownCards.Count);
        Assert.Equal("Vilain 129", hand.Collections[0].PlayerName);
        Assert.Equal(4429, hand.Collections[0].Amount);
        Assert.Equal(300, hand.Rake);
    }
}
