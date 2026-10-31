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
        Assert.Equal(9, result.Hands.Count);
    }

    [Fact]
    public void Every_accepted_hand_balances_and_identifies_the_hero()
    {
        var hands = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicCash6Max)).Hands;

        Assert.All(hands, hand =>
        {
            Assert.Equal(hand.Actions.Sum(a => a.Amount), hand.TotalPot);
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

        Assert.Equal(3, hand.Collections.Count);
        Assert.Equal(PotKind.Main, hand.Collections[0].Kind);
        Assert.Equal("Vilain12", hand.Collections[0].PlayerName);
        Assert.Equal(11187, hand.Collections[0].Amount);
        Assert.Equal(PotKind.Side, hand.Collections[1].Kind);
        Assert.Equal(1, hand.Collections[1].SidePotNumber);
        Assert.Equal(1208, hand.Collections[1].Amount);
        Assert.Equal(PotKind.Single, hand.Collections[2].Kind);
        Assert.Equal("Vilain12", hand.Collections[2].PlayerName);
        Assert.Equal(747, hand.Collections[2].Amount);

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

    // --- Tournament golden tests ---

    [Fact]
    public void Every_tournament_hand_is_accepted()
    {
        var result = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicTournamentSko));

        Assert.Empty(result.Errors);
        Assert.Equal(27, result.Hands.Count);
    }

    [Fact]
    public void Every_tournament_hand_balances_and_identifies_hero()
    {
        var hands = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicTournamentSko)).Hands;

        Assert.All(hands, hand =>
        {
            Assert.Equal(hand.Actions.Sum(a => a.Amount), hand.TotalPot);
            Assert.Equal(hand.TotalPot, hand.Collections.Sum(c => c.Amount));
            Assert.Equal("Hero", hand.HeroName);
            Assert.Equal(2, hand.HeroCards.Count);
            Assert.Equal(GameType.Tournament, hand.GameType);
            Assert.Equal("Hyper Blast", hand.TournamentName);
            Assert.Equal("01M67M3300V17WMNQ60F4TT70J", hand.ExternalTournamentId);
            Assert.NotNull(hand.BuyIn);
            Assert.Null(hand.Rake);
            Assert.Null(hand.Stakes);
            Assert.All(hand.Actions, action =>
                Assert.Contains(hand.Seats, seat => seat.PlayerName == action.PlayerName));
        });
    }

    [Fact]
    public void Reads_every_field_of_first_tournament_hand()
    {
        var hand = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicTournamentSko)).Hands[0];

        Assert.Equal(PokerRoom.Betclic, hand.Room);
        Assert.Equal(GameType.Tournament, hand.GameType);
        Assert.Equal("01M68TE24AHP2YVFS3B7YNE6NY", hand.ExternalHandId);
        Assert.Equal("Hyper Blast", hand.TournamentName);
        Assert.Equal("01M67M3300V17WMNQ60F4TT70J", hand.ExternalTournamentId);
        Assert.Equal(new HandBuyIn(5.00m, 0m, "EUR"), hand.BuyIn);
        Assert.True(hand.IsRealMoney);
        Assert.Equal(0, hand.Level);
        Assert.Null(hand.Ante);
        Assert.Equal(10, hand.SmallBlind);
        Assert.Equal(20, hand.BigBlind);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 11, 10, 5, TimeSpan.Zero), hand.StartedAt);
        Assert.Equal(1, hand.ButtonSeat);
        Assert.Equal(1, hand.LineNumber);
        Assert.Null(hand.Rake);
        Assert.Null(hand.Stakes);

        Assert.Equal(4, hand.Seats.Count);
        Assert.Equal(new ParsedSeat(1, "Vilain1", 230, 2.50m), hand.Seats[0]);
        Assert.Equal(new ParsedSeat(2, "Hero", 250, 2.50m), hand.Seats[1]);
        Assert.Equal(new ParsedSeat(4, "Vilain2", 300, 2.50m), hand.Seats[2]);
        Assert.Equal(new ParsedSeat(5, "Vilain3", 2727, 6.10m), hand.Seats[3]);

        Assert.Equal("Hero", hand.HeroName);
        Assert.Equal([new Card(Rank.Nine, Suit.Diamonds), new Card(Rank.Nine, Suit.Spades)], hand.HeroCards);

        // MaxSeats = highest seat number (5).
        Assert.Equal(5, hand.MaxSeats);
        Assert.Equal(520, hand.TotalPot);
    }

    [Fact]
    public void Parses_tournament_side_pot_and_multiple_eliminations()
    {
        // Hand 1: 3-way all-in, main pot + 1st side pot, two players eliminated.
        var hand = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicTournamentSko)).Hands[1];

        Assert.True(hand.Collections.Count >= 2);
        Assert.Equal(PotKind.Main, hand.Collections[0].Kind);
        Assert.Equal(PotKind.Side, hand.Collections[1].Kind);
        Assert.Equal(1, hand.Collections[1].SidePotNumber);
    }

    [Fact]
    public void Parses_tournament_bb_posting_all_in()
    {
        // Hand with BB posting all-in (stack < BB): "Posts BB 10 and is all-in" (line 874 area).
        // That hand has a main pot + side pot with elimination.
        var hands = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicTournamentSko)).Hands;

        // Find the hand where a BB posts all-in (stack 10 < BB 20).
        var hand = hands.First(h =>
            h.Actions.Any(a => a.Type == ParsedActionType.PostBigBlind && a.IsAllIn));

        Assert.Contains(hand.Collections, c => c.Kind == PotKind.Main);
        Assert.Contains(hand.Collections, c => c.Kind == PotKind.Side);
    }

    [Fact]
    public void Parses_tournament_heads_up_with_combined_btn_sb_tag()
    {
        // Last hands in the file are heads-up: "[BTN SB Hero]" or "[BTN SB]".
        var hands = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicTournamentSko)).Hands;

        // Find a hand with exactly 2 seats.
        var huHand = hands.First(h => h.Seats.Count == 2);

        Assert.Equal(2, huHand.MaxSeats);
        Assert.Equal("Hero", huHand.HeroName);
        // In heads-up, BTN = SB.
        Assert.Equal(huHand.ButtonSeat,
            huHand.Seats.First(s => huHand.Actions.Any(a =>
                a.PlayerName == s.PlayerName && a.Type == ParsedActionType.PostSmallBlind)).SeatNumber);
    }

    [Fact]
    public void Tournament_bounty_values_are_parsed()
    {
        var hand = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicTournamentSko)).Hands[0];

        Assert.All(hand.Seats, seat => Assert.NotNull(seat.Bounty));
        Assert.Equal(6.10m, hand.Seats.First(s => s.PlayerName == "Vilain3").Bounty);
    }

    [Fact]
    public void Parses_hero_elimination_and_bounty_award()
    {
        // Hand 0: Hero (9d9s) loses to Vilain3 (TsTh), eliminated 24th.
        var hand = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicTournamentSko)).Hands[0];

        Assert.Single(hand.Eliminations);
        Assert.Equal("Hero", hand.Eliminations[0].PlayerName);
        Assert.Equal(24, hand.Eliminations[0].FinishPosition);

        Assert.Single(hand.BountyAwards);
        Assert.Equal("Vilain3", hand.BountyAwards[0].Winner);
        Assert.Equal("Hero", hand.BountyAwards[0].EliminatedPlayer);
        Assert.Equal(0.50m, hand.BountyAwards[0].Amount);
        Assert.Equal("EUR", hand.BountyAwards[0].Currency);
    }

    [Fact]
    public void Parses_multiple_eliminations_in_single_hand()
    {
        // Hand 1: 3-way all-in, Vilain4 finished 26th + Vilain5 finished 27th, two bounty awards.
        var hand = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicTournamentSko)).Hands[1];

        Assert.Equal(2, hand.Eliminations.Count);
        Assert.Contains(hand.Eliminations, e => e.PlayerName == "Vilain4" && e.FinishPosition == 26);
        Assert.Contains(hand.Eliminations, e => e.PlayerName == "Vilain5" && e.FinishPosition == 27);

        Assert.Equal(2, hand.BountyAwards.Count);
        Assert.All(hand.BountyAwards, b => Assert.Equal("Vilain3", b.Winner));
    }

    [Fact]
    public void Hands_without_elimination_have_empty_lists()
    {
        var hands = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicTournamentSko)).Hands;

        // Most hands have no elimination.
        var noElim = hands.Where(h => h.Eliminations.Count == 0).ToList();
        Assert.True(noElim.Count > hands.Count / 2);
        Assert.All(noElim, h =>
        {
            Assert.Empty(h.Eliminations);
            Assert.Empty(h.BountyAwards);
        });
    }

    [Fact]
    public void Cash_game_hands_have_empty_elimination_lists()
    {
        var hands = parser.Parse(GoldenFiles.ReadBetclic(GoldenFiles.BetclicCash6Max)).Hands;

        Assert.All(hands, hand =>
        {
            Assert.Empty(hand.Eliminations);
            Assert.Empty(hand.BountyAwards);
        });
    }
}
