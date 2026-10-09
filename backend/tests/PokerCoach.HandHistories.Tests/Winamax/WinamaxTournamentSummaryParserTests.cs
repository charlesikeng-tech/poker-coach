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
        var entry = Assert.Single(summary.Entries);
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
        Assert.Equal(1, entry.EntryNumber);
        Assert.Equal(new TimeSpan(2, 18, 22), entry.PlayedDuration);
        Assert.Equal(108, entry.FinishPosition);
        Assert.Equal(10.55m, entry.PrizeWinnings);

        // Not printed: unknown, not zero. Deciding what it means is not the parser's job.
        Assert.Null(entry.BountyWinnings);
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
        var entry = Assert.Single(summary.Entries);
        Assert.Equal(new TimeSpan(2, 52, 15), entry.PlayedDuration);
        Assert.Equal(11, entry.FinishPosition);
        Assert.Equal(25.42m, entry.PrizeWinnings);
        Assert.Equal(18.86m, entry.BountyWinnings);
    }

    [Fact]
    public void Reads_a_late_registration_summary_finished_out_of_the_money()
    {
        var result = parser.Parse(GoldenFiles.Read(GoldenFiles.QuantumSummary));

        Assert.Empty(result.Errors);
        var summary = Assert.IsType<ParsedTournamentSummary>(result.Summary);
        Assert.Equal("1181101290", summary.ExternalTournamentId);
        Assert.Equal("QUANTUM", summary.TournamentName);
        Assert.Equal(3808, summary.RegisteredPlayers);
        Assert.Equal(9480m, summary.PrizePool);
        var entry = Assert.Single(summary.Entries);
        Assert.True(entry.LateRegistration);
        Assert.Equal(new TimeSpan(0, 52, 33), entry.PlayedDuration);
        Assert.Equal(2686, entry.FinishPosition);

        // No "You won" line: not printed means unknown here; the performance module decides it is zero.
        Assert.Null(entry.PrizeWinnings);
        Assert.Null(entry.BountyWinnings);
    }

    [Fact]
    public void Reads_bounties_won_out_of_the_money()
    {
        var result = parser.Parse(GoldenFiles.Read(GoldenFiles.ArcturusSummary));

        Assert.Empty(result.Errors);
        var entry = Assert.Single(Assert.IsType<ParsedTournamentSummary>(result.Summary).Entries);
        Assert.Equal(185, entry.FinishPosition);
        Assert.Equal(1m, entry.BountyWinnings);
        Assert.Null(entry.PrizeWinnings);
        Assert.False(entry.LateRegistration);
    }

    [Fact]
    public void A_re_entry_adds_an_entry_and_the_last_block_gives_the_tournament_figures()
    {
        var result = parser.Parse(GoldenFiles.Read(GoldenFiles.AsteroidSummary));

        Assert.Empty(result.Errors);
        var summary = Assert.IsType<ParsedTournamentSummary>(result.Summary);
        Assert.Equal("1178140542", summary.ExternalTournamentId);
        Assert.Equal(1795, summary.RegisteredPlayers);
        Assert.Equal(4594m, summary.PrizePool);
        ParsedTournamentEntry[] expected =
        [
            new(1, true, new TimeSpan(0, 50, 18), 1151, null, null),
            new(2, true, new TimeSpan(1, 18, 25), 978, null, null),
        ];
        Assert.Equal(expected, summary.Entries);
    }

    [Fact]
    public void Blocks_of_different_tournaments_reject_the_summary()
    {
        var content = GoldenFiles.Read(GoldenFiles.AsteroidSummary);
        var secondHeader = content.IndexOf("Winamax Poker", 10, StringComparison.Ordinal);
        content = content[..secondHeader]
            + content[secondHeader..].Replace("ASTEROID(1178140542)", "ASTEROID(1178140543)", StringComparison.Ordinal);

        var result = parser.Parse(content);

        Assert.Null(result.Summary);
        Assert.Equal(new ParseError(ParseErrorCodes.UnexpectedSection, 15, null), Assert.Single(result.Errors));
    }

    [Fact]
    public void A_summary_without_late_registration_says_so()
    {
        var summary = parser.Parse(GoldenFiles.Read(GoldenFiles.CassiopeiaSummary)).Summary;

        Assert.NotNull(summary);
        Assert.False(Assert.Single(summary.Entries).LateRegistration);
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
    [InlineData(GoldenFiles.AsteroidSummary, 2297)]
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
