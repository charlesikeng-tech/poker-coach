using PokerCoach.HandHistories.Winamax;

namespace PokerCoach.HandHistories.Tests.Winamax;

/// <summary>
/// Untrusted or damaged input: the faulty hand is rejected with a precise code and line, the rest of the
/// file is still imported, and nothing throws.
/// </summary>
public sealed class WinamaxHandHistoryParserRejectionTests
{
    private const string FirstHandId = "4988240872307949569-82-1788566461";
    private const string LastHandId = "4988240872307949569-153-1788569270";

    private readonly WinamaxHandHistoryParser parser = new();
    private readonly string cassiopeia = GoldenFiles.Read(GoldenFiles.CassiopeiaHands);

    [Fact]
    public void A_hand_still_being_written_is_reported_incomplete()
    {
        // A file grows while the tournament runs: the last hand may be cut anywhere.
        var truncated = cassiopeia[..cassiopeia.LastIndexOf("*** SUMMARY ***", StringComparison.Ordinal)];

        var result = parser.Parse(truncated);

        Assert.Equal(71, result.Hands.Count);
        Assert.Equal(new ParseError(ParseErrorCodes.IncompleteHand, 2595, LastHandId), Assert.Single(result.Errors));
    }

    [Fact]
    public void An_unknown_line_rejects_its_hand_only()
    {
        var content = GoldenFiles.ReplaceFirst(
            cassiopeia,
            "*** PRE-FLOP *** \n",
            "*** PRE-FLOP *** \nVillain03 said, \"nice hand\"\n");

        var result = parser.Parse(content);

        Assert.Equal(71, result.Hands.Count);
        Assert.DoesNotContain(result.Hands, hand => hand.ExternalHandId == FirstHandId);
        Assert.Equal(new ParseError(ParseErrorCodes.UnrecognizedLine, 19, FirstHandId), Assert.Single(result.Errors));
    }

    [Fact]
    public void A_pot_that_does_not_balance_rejects_the_hand()
    {
        var content = GoldenFiles.ReplaceFirst(cassiopeia, "Total pot 122090 | No rake", "Total pot 122091 | No rake");

        var result = parser.Parse(content);

        Assert.Equal(new ParseError(ParseErrorCodes.PotMismatch, 40, FirstHandId), Assert.Single(result.Errors));
    }

    [Fact]
    public void A_raise_inconsistent_with_the_current_bet_rejects_the_hand()
    {
        var content = GoldenFiles.ReplaceFirst(cassiopeia, "Villain03 raises 5000 to 6400", "Villain03 raises 4000 to 6400");

        var result = parser.Parse(content);

        Assert.Equal(new ParseError(ParseErrorCodes.InconsistentRaise, 22, FirstHandId), Assert.Single(result.Errors));
    }

    [Fact]
    public void An_invalid_card_rejects_the_hand()
    {
        var content = GoldenFiles.ReplaceFirst(cassiopeia, "Dealt to Hero [8c 2d]", "Dealt to Hero [8x 2d]");

        var result = parser.Parse(content);

        Assert.Equal(new ParseError(ParseErrorCodes.InvalidCard, 17, FirstHandId), Assert.Single(result.Errors));
    }

    [Fact]
    public void An_amount_too_large_for_any_tournament_is_rejected_without_throwing()
    {
        var content = GoldenFiles.ReplaceFirst(
            cassiopeia,
            "Villain05 calls 1400",
            "Villain05 calls 99999999999999999999999999");

        var result = parser.Parse(content);

        Assert.Equal(new ParseError(ParseErrorCodes.InvalidNumber, 19, FirstHandId), Assert.Single(result.Errors));
    }

    [Fact]
    public void An_oversized_line_is_rejected_before_any_pattern_matching()
    {
        var content = GoldenFiles.ReplaceFirst(
            cassiopeia,
            "Villain05 calls 1400",
            "Villain05 calls 1400" + new string(' ', 5) + new string('x', WinamaxHandReader.MaxLineLength));

        var result = parser.Parse(content);

        Assert.Equal(new ParseError(ParseErrorCodes.LineTooLong, 19, FirstHandId), Assert.Single(result.Errors));
    }

    [Fact]
    public void Windows_line_endings_and_a_byte_order_mark_are_accepted()
    {
        var content = "﻿" + cassiopeia.Replace("\n", "\r\n", StringComparison.Ordinal);

        var result = parser.Parse(content);

        Assert.Empty(result.Errors);
        Assert.Equal(72, result.Hands.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n  \n")]
    public void An_empty_file_is_reported(string content)
    {
        var result = parser.Parse(content);

        Assert.Empty(result.Hands);
        Assert.Equal(ParseErrorCodes.EmptyFile, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void A_file_that_is_not_a_hand_history_is_rejected_as_a_whole()
    {
        var result = parser.Parse(GoldenFiles.Read(GoldenFiles.CassiopeiaSummary));

        Assert.Empty(result.Hands);
        Assert.Equal(new ParseError(ParseErrorCodes.UnrecognizedFormat, 1, null), Assert.Single(result.Errors));
    }
}
