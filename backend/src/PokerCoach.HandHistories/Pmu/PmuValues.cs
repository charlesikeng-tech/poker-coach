using System.Globalization;
using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories.Pmu;

internal static class PmuValues
{
    internal const string GamePrefix = "GAME #";
    internal const string HoleCardsMarker = "*** HOLE CARDS ***";
    internal const string SummaryMarker = "*** SUMMARY ***";
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

    internal static bool TryParseEuroCents(string text, out long cents)
    {
        cents = 0;
        return text.Length > 1 && text[0] == '€' && TryParseCents(text[1..], out cents);
    }

    /// <summary>PMU cards: suit-first uppercase then rank, e.g. S6, CQ, H10, DA.</summary>
    internal static bool TryParseCard(string token, out Card card)
    {
        card = default;
        if (token.Length < 2 || token.Length > 3)
        {
            return false;
        }

        var suit = token[0] switch
        {
            'S' => (Suit?)Suit.Spades,
            'C' => (Suit?)Suit.Clubs,
            'D' => (Suit?)Suit.Diamonds,
            'H' => (Suit?)Suit.Hearts,
            _ => null,
        };

        if (suit is null)
        {
            return false;
        }

        var rankStr = token[1..];
        var rank = rankStr switch
        {
            "2" => (Rank?)Rank.Two,
            "3" => (Rank?)Rank.Three,
            "4" => (Rank?)Rank.Four,
            "5" => (Rank?)Rank.Five,
            "6" => (Rank?)Rank.Six,
            "7" => (Rank?)Rank.Seven,
            "8" => (Rank?)Rank.Eight,
            "9" => (Rank?)Rank.Nine,
            "10" => (Rank?)Rank.Ten,
            "J" => (Rank?)Rank.Jack,
            "Q" => (Rank?)Rank.Queen,
            "K" => (Rank?)Rank.King,
            "A" => (Rank?)Rank.Ace,
            _ => null,
        };

        if (rank is null)
        {
            return false;
        }

        card = new Card(rank.Value, suit.Value);
        return true;
    }

    internal static bool TryParseCards(string text, out List<Card> cards)
    {
        cards = [];
        foreach (var token in text.Split(' '))
        {
            if (!TryParseCard(token, out var card))
            {
                return false;
            }

            cards.Add(card);
        }

        return cards.Count > 0;
    }

    internal static bool TryParseDateTime(string dateText, string timezone, out DateTimeOffset value)
    {
        value = default;
        if (!DateTime.TryParseExact(dateText, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return false;
        }

        var offset = timezone switch
        {
            "CET" => TimeSpan.FromHours(1),
            "CEST" => TimeSpan.FromHours(2),
            "UTC" => TimeSpan.Zero,
            "GMT" => TimeSpan.Zero,
            _ => TimeSpan.Zero,
        };

        value = new DateTimeOffset(local, offset).ToUniversalTime();
        return true;
    }

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
