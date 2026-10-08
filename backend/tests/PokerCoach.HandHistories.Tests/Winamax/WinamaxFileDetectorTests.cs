using PokerCoach.HandHistories.Winamax;

namespace PokerCoach.HandHistories.Tests.Winamax;

public sealed class WinamaxFileDetectorTests
{
    [Theory]
    [InlineData(GoldenFiles.CassiopeiaHands, HandHistoryFileKind.HandHistory)]
    [InlineData(GoldenFiles.AcceleratorHands, HandHistoryFileKind.HandHistory)]
    [InlineData(GoldenFiles.CassiopeiaSummary, HandHistoryFileKind.TournamentSummary)]
    [InlineData(GoldenFiles.AcceleratorSummary, HandHistoryFileKind.TournamentSummary)]
    public void Detects_the_kind_of_a_real_file(string fileName, HandHistoryFileKind expected) =>
        Assert.Equal(expected, WinamaxFileDetector.Detect(GoldenFiles.Read(fileName)));

    [Theory]
    [InlineData("")]
    [InlineData("\n\n")]
    [InlineData("PokerStars Hand #1: Tournament")]
    public void Anything_else_is_unknown(string content) =>
        Assert.Equal(HandHistoryFileKind.Unknown, WinamaxFileDetector.Detect(content));

    [Fact]
    public void Ignores_a_byte_order_mark_and_leading_blank_lines() =>
        Assert.Equal(
            HandHistoryFileKind.TournamentSummary,
            WinamaxFileDetector.Detect("﻿\r\n\r\nWinamax Poker - Tournament summary : X(1)\r\n"));
}
