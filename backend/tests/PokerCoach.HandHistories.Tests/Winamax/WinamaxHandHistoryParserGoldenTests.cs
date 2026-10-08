using PokerCoach.Domain.Poker;
using PokerCoach.HandHistories.Winamax;

namespace PokerCoach.HandHistories.Tests.Winamax;

/// <summary>
/// Expected values below were produced independently by the reference script used during the format
/// spike (docs/research/winamax-format-notes.md), not by this parser.
/// </summary>
public sealed class WinamaxHandHistoryParserGoldenTests
{
    private readonly WinamaxHandHistoryParser parser = new();

    [Theory]
    [InlineData(GoldenFiles.CassiopeiaHands, 72)]
    [InlineData(GoldenFiles.AcceleratorHands, 222)]
    public void Every_hand_of_a_real_file_is_accepted(string fileName, int expectedHands)
    {
        var result = parser.Parse(GoldenFiles.Read(fileName));

        Assert.Empty(result.Errors);
        Assert.Equal(expectedHands, result.Hands.Count);
    }

    [Theory]
    [InlineData(GoldenFiles.CassiopeiaHands)]
    [InlineData(GoldenFiles.AcceleratorHands)]
    public void Every_accepted_hand_balances_and_identifies_the_hero(string fileName)
    {
        var hands = parser.Parse(GoldenFiles.Read(fileName)).Hands;

        Assert.All(hands, hand =>
        {
            Assert.Equal(hand.TotalPot, hand.Actions.Sum(a => a.Amount));
            Assert.Equal(hand.TotalPot, hand.Collections.Sum(c => c.Amount));
            Assert.Equal("Hero", hand.HeroName);
            Assert.Equal(2, hand.HeroCards.Count);
            Assert.All(hand.Actions, action => Assert.Contains(hand.Seats, seat => seat.PlayerName == action.PlayerName));
        });
    }

    [Fact]
    public void Reads_every_field_of_a_hand()
    {
        var hand = parser.Parse(GoldenFiles.Read(GoldenFiles.CassiopeiaHands)).Hands[0];

        Assert.Equal(PokerRoom.Winamax, hand.Room);
        Assert.Equal("4988240872307949569-82-1788566461", hand.ExternalHandId);
        Assert.Equal("CASSIOPEIA", hand.TournamentName);
        Assert.Equal("1161415333", hand.ExternalTournamentId);
        Assert.Equal(new HandBuyIn(9m, 1m, "EUR"), hand.BuyIn);
        Assert.Equal("CASSIOPEIA(1161415333)#0000", hand.TableName);
        Assert.Equal(6, hand.MaxSeats);
        Assert.True(hand.IsRealMoney);
        Assert.Equal(12, hand.Level);
        Assert.Equal(160, hand.Ante);
        Assert.Equal(700, hand.SmallBlind);
        Assert.Equal(1400, hand.BigBlind);
        Assert.Equal(new DateTimeOffset(2026, 9, 5, 0, 1, 1, TimeSpan.Zero), hand.StartedAt);
        Assert.Equal(2, hand.ButtonSeat);
        Assert.Equal(1, hand.LineNumber);

        Assert.Equal(
            new[]
            {
                new ParsedSeat(1, "Villain01", 47537, 7.50m),
                new ParsedSeat(2, "Hero", 57120, 5m),
                new ParsedSeat(3, "Villain02", 111217, 18.90m),
                new ParsedSeat(4, "Villain03", 52540, 5m),
                new ParsedSeat(5, "Villain04", 104775, 7.20m),
                new ParsedSeat(6, "Villain05", 108400, 21m),
            },
            hand.Seats);

        Assert.Equal("8c 2d", Cards(hand.HeroCards));
        Assert.Equal("5c 2h Ac 7s 9s", Cards(hand.Board));
        Assert.Equal(122090, hand.TotalPot);
        Assert.Equal(new ParsedPotCollection("Villain05", 122090, PotKind.Single, null), Assert.Single(hand.Collections));
        Assert.Empty(hand.ShownCards);
    }

    [Fact]
    public void Reads_actions_with_the_chips_each_one_adds_to_the_pot()
    {
        var actions = parser.Parse(GoldenFiles.Read(GoldenFiles.CassiopeiaHands)).Hands[0].Actions;

        Assert.Equal(21, actions.Count);
        Assert.Equal(new ParsedAction(1, Street.Preflop, "Villain03", ParsedActionType.PostAnte, 160, null, false), actions[0]);
        Assert.Equal(new ParsedAction(6, Street.Preflop, "Villain03", ParsedActionType.PostSmallBlind, 700, null, false), actions[5]);
        Assert.Equal(new ParsedAction(7, Street.Preflop, "Villain04", ParsedActionType.PostBigBlind, 1400, null, false), actions[6]);

        // "raises 5000 to 6400" after posting the 700 small blind: 5 700 more chips, street total 6 400.
        Assert.Equal(new ParsedAction(11, Street.Preflop, "Villain03", ParsedActionType.Raise, 5700, 6400, false), actions[10]);
        Assert.Equal(new ParsedAction(13, Street.Preflop, "Villain05", ParsedActionType.Call, 5000, null, false), actions[12]);
        Assert.Equal(new ParsedAction(14, Street.Flop, "Villain03", ParsedActionType.Check, 0, null, false), actions[13]);
        Assert.Equal(new ParsedAction(17, Street.Turn, "Villain05", ParsedActionType.Bet, 5250, null, false), actions[16]);
        Assert.Equal(new ParsedAction(20, Street.River, "Villain05", ParsedActionType.Bet, 96590, null, true), actions[19]);
        Assert.Equal(new ParsedAction(21, Street.River, "Villain03", ParsedActionType.Fold, 0, null, false), actions[20]);
    }

    [Fact]
    public void A_seated_player_who_was_not_dealt_in_has_no_action()
    {
        var hand = parser.Parse(GoldenFiles.Read(GoldenFiles.CassiopeiaHands)).Hands[0];

        Assert.Contains(hand.Seats, seat => seat.PlayerName == "Villain02");
        Assert.DoesNotContain(hand.Actions, action => action.PlayerName == "Villain02");
    }

    [Fact]
    public void Accepts_a_hand_without_small_blind()
    {
        var hand = Assert.Single(
            parser.Parse(GoldenFiles.Read(GoldenFiles.CassiopeiaHands)).Hands,
            h => h.ExternalHandId == "4988240872307949569-102-1788567383");

        Assert.DoesNotContain(hand.Actions, action => action.Type == ParsedActionType.PostSmallBlind);
        var bigBlind = Assert.Single(hand.Actions, action => action.Type == ParsedActionType.PostBigBlind);
        Assert.Equal(2500, bigBlind.Amount);
    }

    [Fact]
    public void Reads_an_all_in_preflop_showdown()
    {
        var hand = parser.Parse(GoldenFiles.Read(GoldenFiles.CassiopeiaHands)).Hands[^1];

        Assert.Equal("4988240872307949569-153-1788569270", hand.ExternalHandId);
        var raises = hand.Actions.Where(a => a.Type == ParsedActionType.Raise).ToList();
        Assert.Equal(
            new[]
            {
                new ParsedAction(12, Street.Preflop, "Hero", ParsedActionType.Raise, 85958, 85958, true),
                new ParsedAction(13, Street.Preflop, "Villain02", ParsedActionType.Raise, 196202, 200202, true),
            },
            raises);
        Assert.Equal("5h 4h 8d 9h Ks", Cards(hand.Board));
        Assert.Equal(2, hand.ShownCards.Count);
        Assert.Equal("Qc Jc", Cards(hand.ShownCards[0].Cards));
        Assert.Equal("High card : King", hand.ShownCards[0].HandDescription);
        Assert.Equal(new ParsedPotCollection("Villain02", 300160, PotKind.Single, null), Assert.Single(hand.Collections));
    }

    [Fact]
    public void Reads_main_and_side_pots()
    {
        var hand = Assert.Single(
            parser.Parse(GoldenFiles.Read(GoldenFiles.AcceleratorHands)).Hands,
            h => h.ExternalHandId == "5021920691583189039-29-1789518629");

        // The 620 "side pot" is an uncalled excess returned to its owner: Winamax prints no separate return.
        Assert.Equal(
            new[]
            {
                new ParsedPotCollection("Villain14", 59402, PotKind.Main, null),
                new ParsedPotCollection("Villain12", 620, PotKind.Side, 1),
            },
            hand.Collections);
        Assert.Equal(60022, hand.TotalPot);
    }

    [Fact]
    public void Accepts_cards_shown_voluntarily_without_a_showdown()
    {
        var hand = Assert.Single(
            parser.Parse(GoldenFiles.Read(GoldenFiles.AcceleratorHands)).Hands,
            h => h.ExternalHandId == "5021920691583188998-161-1789525338");

        var shown = Assert.Single(hand.ShownCards);
        Assert.Equal("Villain-39", shown.PlayerName);
        Assert.Equal("3h As", Cards(shown.Cards));
        Assert.Equal(new ParsedPotCollection("Villain-39", 190350, PotKind.Single, null), Assert.Single(hand.Collections));
    }

    [Fact]
    public void Reads_player_names_containing_spaces_dots_and_dashes()
    {
        var hands = parser.Parse(GoldenFiles.Read(GoldenFiles.AcceleratorHands)).Hands;
        var actingPlayers = hands.SelectMany(h => h.Actions).Select(a => a.PlayerName).Distinct().ToList();

        Assert.Contains("Villain 08 x", actingPlayers);
        Assert.Contains(".Villain07", actingPlayers);
        Assert.Contains("Villain-39", actingPlayers);
        Assert.Contains("Villain.11", actingPlayers);
    }

    [Fact]
    public void Hero_bounty_increases_add_up_to_the_bounty_cash_of_the_summary()
    {
        // Progressive KO: half of each eliminated player's bounty is paid in cash, half added to the
        // winner's head. With complete hand coverage the cash won equals the head growth.
        var heroBounties = parser.Parse(GoldenFiles.Read(GoldenFiles.AcceleratorHands)).Hands
            .Select(h => h.Seats.Single(s => s.PlayerName == "Hero").Bounty ?? 0m)
            .ToList();

        var increases = heroBounties.Zip(heroBounties.Skip(1), (before, after) => Math.Max(0m, after - before)).Sum();

        Assert.Equal(2.50m, heroBounties[0]);
        Assert.Equal(21.36m, heroBounties[^1]);
        Assert.Equal(18.86m, increases);
    }

    private static string Cards(IEnumerable<Card> cards) => string.Join(' ', cards);
}
