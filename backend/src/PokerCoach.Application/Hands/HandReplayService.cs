using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Application.Hands;

/// <summary>A stored hand as the replayer needs it, with player names still inside (they never leave the server).</summary>
/// <param name="Index">Position of the hand in the tournament, from 1, in play order.</param>
/// <param name="Shown">Cards shown at showdown, by player.</param>
public sealed record HandReplaySource(
    Guid HandId,
    Guid TournamentId,
    string TournamentName,
    DateTimeOffset StartedAt,
    int Level,
    long SmallBlind,
    long BigBlind,
    long? Ante,
    int MaxSeats,
    string? HeroCards,
    HandForAnalysis Hand,
    IReadOnlyList<Card> Board,
    IReadOnlyDictionary<string, IReadOnlyList<Card>> Shown,
    int Index,
    int HandCount,
    Guid? PreviousHandId,
    Guid? NextHandId);

public interface IHandReplayStore
{
    /// <summary>The hand if it belongs to one of the user's confirmed poker accounts; otherwise null.</summary>
    Task<HandReplaySource?> FindAsync(Guid userId, Guid handId, CancellationToken cancellationToken);
}

/// <summary>A seat at the table. Players are named by position only: other players' pseudonyms stay private.</summary>
/// <param name="Position">Null for a seated player who was not dealt in, or beyond 9 players.</param>
/// <param name="Cards">The hero's cards, or a player's cards shown at showdown; null when unknown.</param>
/// <param name="Collected">Chips collected from the pot(s), uncalled bets included.</param>
public sealed record ReplaySeat(
    int SeatNumber,
    PokerPosition? Position,
    bool IsHero,
    bool IsDealt,
    long Stack,
    IReadOnlyList<Card>? Cards,
    long Collected);

/// <param name="Amount">Chips this action adds to the pot (a raise's increment, not its total).</param>
public sealed record ReplayAction(Street Street, int SeatNumber, ActionKind Kind, long Amount, bool IsAllIn);

public sealed record HandReplay(
    Guid HandId,
    Guid TournamentId,
    string TournamentName,
    DateTimeOffset StartedAt,
    int Level,
    long SmallBlind,
    long BigBlind,
    long? Ante,
    int MaxSeats,
    int ButtonSeat,
    IReadOnlyList<ReplaySeat> Seats,
    IReadOnlyList<ReplayAction> Actions,
    IReadOnlyList<Card> Board,
    int Index,
    int HandCount,
    Guid? PreviousHandId,
    Guid? NextHandId);

/// <summary>
/// Turns a stored hand into the replayer's view: seats by number and position, actions by seat. Names are
/// dropped here, so no other player's pseudonym reaches the browser.
/// </summary>
public sealed class HandReplayService(IHandReplayStore store)
{
    public async Task<HandReplay?> GetAsync(Guid userId, Guid handId, CancellationToken cancellationToken)
    {
        var source = await store.FindAsync(userId, handId, cancellationToken);
        return source is null ? null : Build(source);
    }

    internal static HandReplay Build(HandReplaySource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var hand = source.Hand;
        var positions = PositionResolver.Resolve(hand);
        var dealt = hand.Actions.Select(a => a.Player).ToHashSet(StringComparer.Ordinal);
        var seatOf = hand.Seats.ToDictionary(s => s.Player, s => s.SeatNumber, StringComparer.Ordinal);

        var seats = hand.Seats
            .OrderBy(s => s.SeatNumber)
            .Select(s =>
            {
                var isHero = s.Player == hand.Hero;
                IReadOnlyList<Card>? cards = isHero ? HeroCards(source.HeroCards) : null;
                if (source.Shown.TryGetValue(s.Player, out var shown) && shown.Count > 0)
                {
                    cards = shown;
                }

                return new ReplaySeat(
                    s.SeatNumber,
                    positions.TryGetValue(s.Player, out var position) ? position : null,
                    isHero,
                    dealt.Contains(s.Player),
                    s.Stack,
                    cards,
                    hand.Collected.GetValueOrDefault(s.Player));
            })
            .ToList();

        // An action by someone not seated cannot be placed at the table: the stored hand is inconsistent.
        var actions = hand.Actions
            .Select(a => new ReplayAction(
                a.Street,
                seatOf.TryGetValue(a.Player, out var seat)
                    ? seat
                    : throw new InvalidOperationException("A stored hand has an action by a player who is not seated."),
                a.Kind,
                a.Amount,
                a.IsAllIn))
            .ToList();

        return new HandReplay(
            source.HandId,
            source.TournamentId,
            source.TournamentName,
            source.StartedAt,
            source.Level,
            source.SmallBlind,
            source.BigBlind,
            source.Ante,
            source.MaxSeats,
            hand.ButtonSeat,
            seats,
            actions,
            source.Board,
            source.Index,
            source.HandCount,
            source.PreviousHandId,
            source.NextHandId);
    }

    /// <summary>"AhKd" → [Ah, Kd]; anything unreadable is unknown, never guessed.</summary>
    private static List<Card>? HeroCards(string? text)
    {
        if (text is null || text.Length % 2 != 0 || text.Length == 0)
        {
            return null;
        }

        var cards = new List<Card>();
        for (var i = 0; i < text.Length; i += 2)
        {
            if (!Card.TryParse(text.Substring(i, 2), out var card))
            {
                return null;
            }

            cards.Add(card);
        }

        return cards;
    }
}
