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
                .ToDictionary(g => g.Key, g => g.Sum(c => c.Amount), StringComparer.Ordinal));
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
