using System.Globalization;
using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories.Winamax;

/// <summary>Number, date and card conversions shared by the Winamax parsers. All culture-invariant.</summary>
internal static class WinamaxValues
{
    internal const string Currency = "EUR";
    internal const char EuroSign = '€';
    internal const string HandHeaderPrefix = "Winamax Poker - Tournament \"";
    internal const string SummaryHeaderPrefix = "Winamax Poker - Tournament summary : ";

    /// <summary>
    /// Upper bound for any chip amount. Far above real tournament stacks; it keeps sums of untrusted
    /// amounts far from overflow.
    /// </summary>
    internal const long MaxChipAmount = 1_000_000_000_000;

    private const string DateFormat = "yyyy/MM/dd HH:mm:ss";

    internal static bool TryParseChips(string text, out long value) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)
        && value <= MaxChipAmount;

    internal static bool TryParseInt(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    internal static bool TryParseEuros(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);

    /// <summary>Parses "12.50€".</summary>
    internal static bool TryParseEurosWithSign(string text, out decimal value)
    {
        value = 0;
        return text.Length > 1 && text[^1] == EuroSign && TryParseEuros(text[..^1], out value);
    }

    internal static bool TryParseUtc(string text, out DateTimeOffset value) =>
        DateTimeOffset.TryParseExact(
            text,
            DateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out value);

    /// <summary>Parses space-separated cards ("Ah Kd 2c").</summary>
    internal static bool TryParseCards(string text, out List<Card> cards)
    {
        cards = [];
        foreach (var token in text.Split(' '))
        {
            if (!Card.TryParse(token, out var card))
            {
                return false;
            }

            cards.Add(card);
        }

        return true;
    }

    /// <summary>Splits content into lines, accepting LF or CRLF, a leading BOM and trailing spaces.</summary>
    internal static string[] SplitLines(string content)
    {
        var lines = content.TrimStart('﻿').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd();
        }

        return lines;
    }
}
