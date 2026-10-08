using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories.Winamax;

public sealed class WinamaxHandHistoryProvider : IHandHistoryProvider
{
    public PokerRoom Room => PokerRoom.Winamax;

    public IHandHistoryParser HandHistories { get; } = new WinamaxHandHistoryParser();

    public ITournamentSummaryParser TournamentSummaries { get; } = new WinamaxTournamentSummaryParser();

    public HandHistoryFileKind Detect(string content) => WinamaxFileDetector.Detect(content);
}
