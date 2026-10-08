namespace PokerCoach.HandHistories.Winamax;

/// <summary>Tells which parser a Winamax file needs, from its first non-empty line (never from its name).</summary>
public static class WinamaxFileDetector
{
    public static HandHistoryFileKind Detect(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var reader = new StringReader(content.TrimStart('﻿'));
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith(WinamaxValues.SummaryHeaderPrefix, StringComparison.Ordinal))
            {
                return HandHistoryFileKind.TournamentSummary;
            }

            return line.StartsWith(WinamaxValues.HandHeaderPrefix, StringComparison.Ordinal)
                ? HandHistoryFileKind.HandHistory
                : HandHistoryFileKind.Unknown;
        }

        return HandHistoryFileKind.Unknown;
    }
}
