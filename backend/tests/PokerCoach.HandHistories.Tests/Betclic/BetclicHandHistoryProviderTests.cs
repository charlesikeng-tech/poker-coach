using PokerCoach.HandHistories.Betclic;

namespace PokerCoach.HandHistories.Tests.Betclic;

public sealed class BetclicHandHistoryProviderTests
{
    private readonly BetclicHandHistoryProvider provider = new();

    [Fact]
    public void Detects_a_betclic_cash_game_file()
    {
        var content = GoldenFiles.ReadBetclic(GoldenFiles.BetclicCash6Max);
        Assert.Equal(HandHistoryFileKind.HandHistory, provider.Detect(content));
    }

    [Fact]
    public void Does_not_detect_a_winamax_file()
    {
        var content = GoldenFiles.Read(GoldenFiles.CassiopeiaHands);
        Assert.Equal(HandHistoryFileKind.Unknown, provider.Detect(content));
    }

    [Fact]
    public void Does_not_detect_empty_content()
    {
        Assert.Equal(HandHistoryFileKind.Unknown, provider.Detect(""));
        Assert.Equal(HandHistoryFileKind.Unknown, provider.Detect("   \n\n"));
    }

    [Fact]
    public void Reads_table_sequence_from_ulid()
    {
        Assert.True(provider.TryReadTableSequence("01M4VENZVZXSTKSZR9K4G364P1", out var sequence));
        Assert.NotNull(sequence);
        Assert.Equal("01M4VENZVZXSTKSZR9K4G364P1", sequence.TableKey);
        Assert.True(sequence.HandNumber > 0);
    }

    [Fact]
    public void Rejects_invalid_hand_id()
    {
        Assert.False(provider.TryReadTableSequence("not-a-ulid", out _));
        Assert.False(provider.TryReadTableSequence("", out _));
    }
}
