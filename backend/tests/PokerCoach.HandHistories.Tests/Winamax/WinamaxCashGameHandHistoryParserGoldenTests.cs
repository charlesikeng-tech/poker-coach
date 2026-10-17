using PokerCoach.Domain.Poker;
using PokerCoach.HandHistories.Winamax;

namespace PokerCoach.HandHistories.Tests.Winamax;

public sealed class WinamaxCashGameHandHistoryParserGoldenTests
{
    private readonly WinamaxHandHistoryParser parser = new();

    [Fact]
    public void Every_hand_of_the_cash_game_file_is_accepted()
    {
        var result = parser.Parse(GoldenFiles.Read(GoldenFiles.AalenCashHands));

        Assert.Empty(result.Errors);
        Assert.Equal(64, result.Hands.Count);
    }

    [Fact]
    public void Every_accepted_cash_hand_balances_and_identifies_the_hero()
    {
        var hands = parser.Parse(GoldenFiles.Read(GoldenFiles.AalenCashHands)).Hands;

        Assert.All(hands, hand =>
        {
            // TotalPot is the gross pot: invested = collected + rake (ParsedHand contract).
            Assert.Equal(hand.TotalPot, hand.Actions.Sum(a => a.Amount));
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
    public void Reads_every_field_of_the_first_hand()
    {
        var hand = parser.Parse(GoldenFiles.Read(GoldenFiles.AalenCashHands)).Hands[0];

        Assert.Equal(PokerRoom.Winamax, hand.Room);
        Assert.Equal(GameType.CashGame, hand.GameType);
        Assert.Equal("23060035-715-1792240332", hand.ExternalHandId);
        Assert.Null(hand.TournamentName);
        Assert.Null(hand.ExternalTournamentId);
        Assert.Null(hand.BuyIn);
        Assert.Equal(5, hand.MaxSeats);
        Assert.True(hand.IsRealMoney);
        Assert.Equal(0, hand.Level);
        Assert.Null(hand.Ante);
        Assert.Equal(1, hand.SmallBlind);   // 0.01€ → 1 cent
        Assert.Equal(2, hand.BigBlind);     // 0.02€ → 2 cents
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 12, 32, 12, TimeSpan.Zero), hand.StartedAt);
        Assert.Equal(2, hand.ButtonSeat);
        Assert.Equal(1, hand.LineNumber);
        Assert.Equal("0.01/0.02", hand.Stakes);

        Assert.Equal(5, hand.Seats.Count);
        Assert.Equal(new ParsedSeat(5, "Hero", 200, null), hand.Seats[4]); // 2€ → 200 cents

        Assert.Equal("Hero", hand.HeroName);
        Assert.Equal(2, hand.HeroCards.Count);
        Assert.Equal(new Card(Rank.Six, Suit.Diamonds), hand.HeroCards[0]);
        Assert.Equal(new Card(Rank.Jack, Suit.Hearts), hand.HeroCards[1]);

        // Gross pot: 1 (SB) + 2 (BB) + 2 (OOP BB) + 3 (Hero bet) + 10 (Villain04 raise total) = 18ct
        // Net pot shown in file: 17ct, rake: 1ct → TotalPot (gross) = 18
        Assert.Equal(18, hand.TotalPot);
        Assert.Equal(1, hand.Rake);
        Assert.Single(hand.Collections);
        Assert.Equal(17, hand.Collections[0].Amount); // net = 17ct
    }

    [Fact]
    public void Reads_a_no_rake_pot()
    {
        var hand = parser.Parse(GoldenFiles.Read(GoldenFiles.AalenCashHands)).Hands[1];

        Assert.Equal(0, hand.Rake);
        Assert.Equal(7, hand.TotalPot);  // 0.07€ = 7ct, no rake so gross = net
    }

    [Fact]
    public void Reads_a_showdown_with_shown_cards()
    {
        // Hand #728: arnow09 shows [9s 2s] and titinantet shows [Kc Tc]
        var hand = parser.Parse(GoldenFiles.Read(GoldenFiles.AalenCashHands)).Hands
            .Single(h => h.ExternalHandId == "23060035-728-1792240788");

        Assert.Equal(2, hand.ShownCards.Count);
        Assert.Equal("Two pairs : Aces and 2", hand.ShownCards[0].HandDescription);
        Assert.Equal("Two pairs : Aces and Kings", hand.ShownCards[1].HandDescription);
        Assert.Equal(5, hand.TotalPot);  // 0.05€ = 5ct
        Assert.Equal(0, hand.Rake);
    }
}
