using System.Diagnostics.CodeAnalysis;
using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories.Betclic;

public sealed class BetclicHandHistoryProvider : IHandHistoryProvider
{
    public PokerRoom Room => PokerRoom.Betclic;

    public IHandHistoryParser HandHistories { get; } = new BetclicHandHistoryParser();

    public ITournamentSummaryParser TournamentSummaries { get; } = new BetclicTournamentSummaryParser();

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

            if (trimmed == BetclicValues.HeaderMarker)
            {
                // Confirm next non-empty line is Site: Betclic.fr.
                while (reader.ReadLine() is { } next)
                {
                    var nextTrimmed = next.TrimEnd();
                    if (nextTrimmed.Length == 0)
                    {
                        continue;
                    }

                    return nextTrimmed.StartsWith(BetclicValues.SitePrefix, StringComparison.Ordinal)
                        ? HandHistoryFileKind.HandHistory
                        : HandHistoryFileKind.Unknown;
                }
            }

            return HandHistoryFileKind.Unknown;
        }

        return HandHistoryFileKind.Unknown;
    }

    /// <summary>
    /// Betclic Hand IDs are ULIDs (timestamp-prefixed, Crockford base32). The first 10 characters
    /// encode milliseconds since Unix epoch, providing a sortable sequence within a table.
    /// </summary>
    public bool TryReadTableSequence(string externalHandId, [NotNullWhen(true)] out TableSequence? sequence)
    {
        ArgumentNullException.ThrowIfNull(externalHandId);
        sequence = null;

        if (externalHandId.Length != 26)
        {
            return false;
        }

        if (!TryDecodeUlidTimestamp(externalHandId, out var timestamp))
        {
            return false;
        }

        sequence = new TableSequence(externalHandId, timestamp);
        return true;
    }

    private static bool TryDecodeUlidTimestamp(string ulid, out long milliseconds)
    {
        milliseconds = 0;
        const string crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        for (var i = 0; i < 10; i++)
        {
            var index = crockford.IndexOf(char.ToUpperInvariant(ulid[i]));
            if (index < 0)
            {
                return false;
            }

            milliseconds = (milliseconds << 5) | (uint)index;
        }

        return true;
    }
}

internal sealed class BetclicTournamentSummaryParser : ITournamentSummaryParser
{
    public PokerRoom Room => PokerRoom.Betclic;

    public TournamentSummaryParseResult Parse(string content) =>
        new(null, [new ParseError(ParseErrorCodes.UnrecognizedFormat, 1, null)]);
}
