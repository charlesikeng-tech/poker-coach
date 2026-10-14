using System.Globalization;
using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories.Betclic;

internal static class BetclicValues
{
    internal const string HeaderMarker = "*** HEADER ***";
    internal const string PlayersMarker = "*** PLAYERS ***";
    internal const string HoleCardsMarker = "*** HOLE CARDS ***";
    internal const string PreflopMarker = "*** PRE-FLOP ***";
    internal const string ShowdownMarker = "*** SHOWDOWN ***";
    internal const string SummaryMarker = "*** SUMMARY ***";

    internal const string SitePrefix = "Site: Betclic.fr";
    internal const string Currency = "EUR";
    internal const char EuroSign = '€';

    internal const long MaxChipAmount = 1_000_000_000_000;

    internal static bool TryParseCents(string text, out long cents)
    {
        cents = 0;
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var euros)
            || euros < 0m || euros > MaxChipAmount)
        {
            return false;
        }

        cents = (long)Math.Round(euros * 100m);
        return true;
    }

    internal static bool TryParseEuroAmount(string text, out long cents)
    {
        cents = 0;
        return text.Length > 1 && text[0] == EuroSign && TryParseCents(text[1..], out cents);
    }

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

    internal static bool TryParseUtc(string text, out DateTimeOffset value) =>
        DateTimeOffset.TryParseExact(
            text,
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out value);

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
