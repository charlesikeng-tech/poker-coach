using System.Diagnostics.CodeAnalysis;
using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories.Pmu;

public sealed class PmuHandHistoryProvider : IHandHistoryProvider
{
    public PokerRoom Room => PokerRoom.Pmu;

    public IHandHistoryParser HandHistories { get; } = new PmuHandHistoryParser();

    public ITournamentSummaryParser TournamentSummaries { get; } = new PmuTournamentSummaryParser();

    public HandHistoryFileKind Detect(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var reader = new StringReader(content.TrimStart('﻿'));
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.TrimEnd();
            if (trimmed.Length == 0)
            {
                continue;
            }

            return trimmed.StartsWith(PmuValues.GamePrefix, StringComparison.Ordinal)
                ? HandHistoryFileKind.HandHistory
                : HandHistoryFileKind.Unknown;
        }

        return HandHistoryFileKind.Unknown;
    }

    public bool TryReadTableSequence(string externalHandId, [NotNullWhen(true)] out TableSequence? sequence)
    {
        ArgumentNullException.ThrowIfNull(externalHandId);
        sequence = null;

        if (!long.TryParse(externalHandId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id))
        {
            return false;
        }

        sequence = new TableSequence(externalHandId, id);
        return true;
    }
}

internal sealed class PmuTournamentSummaryParser : ITournamentSummaryParser
{
    public PokerRoom Room => PokerRoom.Pmu;

    public TournamentSummaryParseResult Parse(string content) =>
        new(null, [new ParseError(ParseErrorCodes.UnrecognizedFormat, 1, null)]);
}
