using PokerCoach.Domain.Poker;
using PokerCoach.HandHistories.Winamax;

namespace PokerCoach.HandHistories.Tests.Winamax;

public sealed class WinamaxTournamentSummaryParserTests
{
    private readonly WinamaxTournamentSummaryParser parser = new();

    [Fact]
    public void Reads_a_knockout_summary_without_bounty_won()
    {
        var result = parser.Parse(GoldenFiles.Read(GoldenFiles.CassiopeiaSummary));

        Assert.Empty(result.Errors);
        var summary = Assert.IsType<ParsedTournamentSummary>(result.Summary);
        Assert.Equal(PokerRoom.Winamax, summary.Room);
        Assert.Equal("1161415333", summary.ExternalTournamentId);
        Assert.Equal("CASSIOPEIA", summary.TournamentName);
        Assert.Equal("Hero", summary.PlayerName);
        Assert.Equal(4m, summary.PrizePoolBuyIn);
        Assert.Equal(5m, summary.BountyBuyIn);
        Assert.Equal(1m, summary.Fee);
        Assert.Equal("EUR", summary.Currency);
        Assert.Equal(994, summary.RegisteredPlayers);
        Assert.Equal("tt", summary.Mode);
        Assert.Equal("knockout", summary.Type);
        Assert.Equal("semiturbo", summary.Speed);
        Assert.Equal("0", summary.FlightId);
        Assert.Equal(4956m, summary.PrizePool);
        Assert.Equal(new DateTimeOffset(2026, 9, 4, 22, 30, 0, TimeSpan.Zero), summary.StartedAt);
        Assert.Equal(new TimeSpan(2, 18, 22), summary.PlayedDuration);
        Assert.Equal(108, summary.FinishPosition);
        Assert.Equal(10.55m, summary.PrizeWinnings);

        // Not printed: unknown, not zero. Deciding what it means is not the parser's job.
        Assert.Null(summary.BountyWinnings);
    }

    [Fact]
    public void Reads_prize_and_bounty_winnings_separately()
    {
        var summary = parser.Parse(GoldenFiles.Read(GoldenFiles.AcceleratorSummary)).Summary;

        Assert.NotNull(summary);
        Assert.Equal(2m, summary.PrizePoolBuyIn);
        Assert.Equal(2.50m, summary.BountyBuyIn);
        Assert.Equal(0.50m, summary.Fee);
        Assert.Equal(753, summary.RegisteredPlayers);
        Assert.Equal(1928m, summary.PrizePool);
        Assert.Equal(new TimeSpan(2, 52, 15), summary.PlayedDuration);
        Assert.Equal(11, summary.FinishPosition);
        Assert.Equal(25.42m, summary.PrizeWinnings);
        Assert.Equal(18.86m, summary.BountyWinnings);
    }

    [Fact]
    public void Reads_a_late_registration_summary_finished_out_of_the_money()
    {
        var result = parser.Parse(GoldenFiles.Read(GoldenFiles.QuantumSummary));

        Assert.Empty(result.Errors);
        var summary = Assert.IsType<ParsedTournamentSummary>(result.Summary);
        Assert.Equal("1181101290", summary.ExternalTournamentId);
        Assert.Equal("QUANTUM", summary.TournamentName);
        Assert.True(summary.LateRegistration);
        Assert.Equal(3808, summary.RegisteredPlayers);
        Assert.Equal(9480m, summary.PrizePool);
        Assert.Equal(new TimeSpan(0, 52, 33), summary.PlayedDuration);
        Assert.Equal(2686, summary.FinishPosition);

        // No "You won" line: not printed means unknown here; the performance module decides it is zero.
        Assert.Null(summary.PrizeWinnings);
        Assert.Null(summary.BountyWinnings);
    }

    [Fact]
    public void Reads_bounties_won_out_of_the_money()
    {
        var result = parser.Parse(GoldenFiles.Read(GoldenFiles.ArcturusSummary));

        Assert.Empty(result.Errors);
        var summary = Assert.IsType<ParsedTournamentSummary>(result.Summary);
        Assert.Equal(185, summary.FinishPosition);
        Assert.Equal(1m, summary.BountyWinnings);
        Assert.Null(summary.PrizeWinnings);
        Assert.False(summary.LateRegistration);
    }

    [Fact]
    public void A_summary_without_late_registration_says_so()
    {
        var summary = parser.Parse(GoldenFiles.Read(GoldenFiles.CassiopeiaSummary)).Summary;

        Assert.NotNull(summary);
        Assert.False(summary.LateRegistration);
    }

    [Fact]
    public void An_unknown_header_suffix_rejects_the_summary()
    {
        var content = GoldenFiles.ReplaceFirst(
            GoldenFiles.Read(GoldenFiles.QuantumSummary),
            " - Late Registration",
            " - Something New");

        var result = parser.Parse(content);

        Assert.Null(result.Summary);
        Assert.Equal(new ParseError(ParseErrorCodes.UnrecognizedFormat, 1, null), Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData(GoldenFiles.QuantumSummary, 4740)]
    [InlineData(GoldenFiles.ArcturusSummary, 659)]
    [InlineData(GoldenFiles.CassiopeiaSummary, 1239)]
    [InlineData(GoldenFiles.AcceleratorSummary, 964)]
    public void The_prize_pool_is_a_whole_number_of_prize_pool_buy_ins(string fileName, int entries)
    {
        // Format knowledge this parser relies on: the first buy-in part is the prize-pool share. Entries
        // (re-entries included) can be inferred from it, while registered players exclude re-entries.
        var summary = parser.Parse(GoldenFiles.Read(fileName)).Summary;

        Assert.NotNull(summary);
        Assert.Equal(entries * summary.PrizePoolBuyIn, summary.PrizePool);
        Assert.True(entries > summary.RegisteredPlayers);
    }

    [Fact]
    public void A_missing_mandatory_field_rejects_the_summary()
    {
        var content = GoldenFiles.ReplaceFirst(GoldenFiles.Read(GoldenFiles.AcceleratorSummary), "Prizepool : 1928€\n", string.Empty);

        var result = parser.Parse(content);

        Assert.Null(result.Summary);
        Assert.Equal(ParseErrorCodes.MissingField, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void An_unknown_line_rejects_the_summary()
    {
        var content = GoldenFiles.ReplaceFirst(
            GoldenFiles.Read(GoldenFiles.AcceleratorSummary),
            "You finished in 11th place",
            "You finished in 11th place\nYou won a ticket");

        var result = parser.Parse(content);

        Assert.Null(result.Summary);
        Assert.Equal(new ParseError(ParseErrorCodes.UnrecognizedLine, 14, null), Assert.Single(result.Errors));
    }

    [Fact]
    public void A_hand_history_is_not_a_summary()
    {
        var result = parser.Parse(GoldenFiles.Read(GoldenFiles.CassiopeiaHands));

        Assert.Null(result.Summary);
        Assert.Equal(new ParseError(ParseErrorCodes.UnrecognizedFormat, 1, null), Assert.Single(result.Errors));
    }
}
