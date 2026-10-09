using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Tournaments;
using PokerCoach.HandHistories.Winamax;

namespace PokerCoach.Application.Tests.Tournaments;

public sealed class TournamentCoverageTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);
    private static readonly SummaryFacts NoSummary = new(null, null, null);

    [Fact]
    public void A_continuous_run_to_the_bust_is_complete()
    {
        var coverage = TournamentCoverage.Analyze(
            [Point(0, 1, 1, 20_000, -500), Point(1, 1, 2, 19_500, 1_000), Point(2, 2, 3, 20_500, -20_500)],
            new SummaryFacts(1, false, 500));

        Assert.Equal(CoverageStatus.Complete, coverage.Status);
        Assert.Equal((1, 2), (coverage.FirstLevel!.Value, coverage.LastLevel!.Value));
        Assert.True(coverage.EndSeen);
        Assert.False(coverage.StartMissing);
    }

    [Fact]
    public void Skipped_hand_numbers_and_stack_jumps_make_it_partial()
    {
        var coverage = TournamentCoverage.Analyze(
            [Point(0, 1, 10, 20_000, 0), Point(1, 1, 14, 18_000, -18_000)],
            NoSummary);

        Assert.Equal(CoverageStatus.Partial, coverage.Status);
        Assert.Equal(3, coverage.MissingHands);
        Assert.Equal(1, coverage.StackBreaks);
        Assert.Null(coverage.StartMissing);
    }

    [Fact]
    public void A_bust_then_a_new_stack_is_a_re_entry_not_a_break()
    {
        var coverage = TournamentCoverage.Analyze(
            [Point(0, 1, 1, 20_000, -20_000), Point(1, 2, 1, 20_000, -20_000, table: "B")],
            new SummaryFacts(2, true, 900));

        Assert.Equal(2, coverage.EntriesSeen);
        Assert.Equal(0, coverage.StackBreaks);
        Assert.Equal(CoverageStatus.Complete, coverage.Status);
    }

    [Fact]
    public void On_time_registration_with_a_first_hand_past_level_one_means_the_start_is_missing()
    {
        var coverage = TournamentCoverage.Analyze([Point(0, 12, 82, 57_120, -57_120)], new SummaryFacts(1, false, 108));

        Assert.True(coverage.StartMissing);
        Assert.Equal(CoverageStatus.Partial, coverage.Status);
    }

    [Fact]
    public void No_hand_is_no_coverage() =>
        Assert.Equal(CoverageStatus.NoHands, TournamentCoverage.Analyze([], NoSummary).Status);

    [Theory]
    [InlineData("20260916_ACCELERATOR_1169257027__real_holdem_no-limit.txt", 7)]
    [InlineData("20260905_CASSIOPEIA_1161415333__real_holdem_no-limit.txt", 0)]
    [InlineData("20261003_ASTEROID_1178140542__real_holdem_no-limit.txt", 0)]
    public void Real_files_known_gaps(string file, int missingHands)
    {
        var coverage = TournamentCoverage.Analyze(RealPoints(file), NoSummary);

        Assert.Equal(missingHands, coverage.MissingHands);
    }

    [Fact]
    public void Real_re_entry_is_seen_and_complete()
    {
        var coverage = TournamentCoverage.Analyze(
            RealPoints("20261003_ASTEROID_1178140542__real_holdem_no-limit.txt"),
            new SummaryFacts(2, true, 978));

        Assert.Equal(2, coverage.EntriesSeen);
        Assert.Equal(0, coverage.StackBreaks);
        Assert.True(coverage.EndSeen);
        Assert.Equal(CoverageStatus.Complete, coverage.Status);
    }

    [Fact]
    public void Real_late_start_is_flagged()
    {
        var coverage = TournamentCoverage.Analyze(
            RealPoints("20260905_CASSIOPEIA_1161415333__real_holdem_no-limit.txt"),
            new SummaryFacts(1, false, 108));

        Assert.Equal(12, coverage.FirstLevel);
        Assert.True(coverage.StartMissing);
        Assert.Equal(CoverageStatus.Partial, coverage.Status);
    }

    [Theory]
    [InlineData("5060075097981714555-23-1791031013", "5060075097981714555", 23)]
    public void Winamax_hand_ids_carry_the_table_sequence(string id, string table, long number)
    {
        Assert.True(new WinamaxHandHistoryProvider().TryReadTableSequence(id, out var sequence));
        Assert.Equal(table, sequence.TableKey);
        Assert.Equal(number, sequence.HandNumber);
        Assert.False(new WinamaxHandHistoryProvider().TryReadTableSequence("not-an-id", out _));
    }

    private static HandPoint Point(int minute, int level, long number, long stack, long net, string table = "A") =>
        new(Start.AddMinutes(minute), level, table, number, stack, net);

    private static List<HandPoint> RealPoints(string file)
    {
        var provider = new WinamaxHandHistoryProvider();
        var content = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", "Winamax", file));
        return provider.HandHistories.Parse(content).Hands
            .Select(hand =>
            {
                var facts = HandAnalyzer.Analyze(HandForAnalysisMapper.From(hand)!);
                provider.TryReadTableSequence(hand.ExternalHandId, out var sequence);
                var stack = hand.Seats.Single(s => s.PlayerName == hand.HeroName).Stack;
                return new HandPoint(hand.StartedAt, hand.Level, sequence?.TableKey, sequence?.HandNumber, stack, facts.NetChips);
            })
            .ToList();
    }
}
