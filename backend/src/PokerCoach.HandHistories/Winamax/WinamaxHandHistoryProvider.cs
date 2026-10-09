using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories.Winamax;

public sealed class WinamaxHandHistoryProvider : IHandHistoryProvider
{
    public PokerRoom Room => PokerRoom.Winamax;

    public IHandHistoryParser HandHistories { get; } = new WinamaxHandHistoryParser();

    public ITournamentSummaryParser TournamentSummaries { get; } = new WinamaxTournamentSummaryParser();

    public HandHistoryFileKind Detect(string content) => WinamaxFileDetector.Detect(content);

    /// <summary>Winamax hand ids are "{table instance}-{hand number on that table}-{timestamp}".</summary>
    public bool TryReadTableSequence(string externalHandId, [NotNullWhen(true)] out TableSequence? sequence)
    {
        ArgumentNullException.ThrowIfNull(externalHandId);
        sequence = null;
        var parts = externalHandId.Split('-');
        if (parts.Length != 3 || parts[0].Length == 0 || !parts[0].All(char.IsAsciiDigit)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        sequence = new TableSequence(parts[0], number);
        return true;
    }
}
