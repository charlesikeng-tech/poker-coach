using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.HandHistories;

namespace PokerCoach.Application.Statistics;

/// <summary>Parsed hand (provider adapter output) to the analysis input of the domain.</summary>
public static class HandForAnalysisMapper
{
    /// <returns>Null when the hand has no hero (such hands are never imported).</returns>
    public static HandForAnalysis? From(ParsedHand hand)
    {
        ArgumentNullException.ThrowIfNull(hand);
        if (hand.HeroName is null)
        {
            return null;
        }

        return new HandForAnalysis(
            hand.ButtonSeat,
            hand.BigBlind,
            hand.HeroName,
            hand.Seats.Select(s => new SeatState(s.SeatNumber, s.PlayerName, s.Stack)).ToList(),
            hand.Actions.Select(a => new HandAction(a.Street, a.PlayerName, Map(a.Type), a.Amount, a.IsAllIn)).ToList(),
            hand.Collections
                .GroupBy(c => c.PlayerName, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Sum(c => c.Amount), StringComparer.Ordinal),
            KnownCards(hand.HeroName, hand.HeroCards, hand.ShownCards.Select(s => (s.PlayerName, s.Cards))));
    }

    /// <summary>
    /// Hole cards known per player: the hero's own, then those shown. Anything but exactly two cards
    /// (a single card flashed, a garbled line) is left out: an unknown hand stays unknown.
    /// </summary>
    public static IReadOnlyDictionary<string, (Card First, Card Second)> KnownCards(
        string hero,
        IReadOnlyList<Card> heroCards,
        IEnumerable<(string Player, IReadOnlyList<Card> Cards)> shown)
    {
        ArgumentNullException.ThrowIfNull(heroCards);
        ArgumentNullException.ThrowIfNull(shown);
        var known = new Dictionary<string, (Card First, Card Second)>(StringComparer.Ordinal);
        foreach (var (player, cards) in shown)
        {
            if (cards.Count == 2)
            {
                known[player] = (cards[0], cards[1]);
            }
        }

        if (heroCards.Count == 2)
        {
            known[hero] = (heroCards[0], heroCards[1]);
        }

        return known;
    }

    public static ActionKind Map(ParsedActionType type) => type switch
    {
        ParsedActionType.PostAnte => ActionKind.PostAnte,
        ParsedActionType.PostSmallBlind => ActionKind.PostSmallBlind,
        ParsedActionType.PostBigBlind => ActionKind.PostBigBlind,
        ParsedActionType.Fold => ActionKind.Fold,
        ParsedActionType.Check => ActionKind.Check,
        ParsedActionType.Call => ActionKind.Call,
        ParsedActionType.Bet => ActionKind.Bet,
        ParsedActionType.Raise => ActionKind.Raise,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
