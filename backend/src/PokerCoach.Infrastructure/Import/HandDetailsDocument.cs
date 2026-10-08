using System.Text.Json;
using System.Text.Json.Serialization;
using PokerCoach.Domain.Poker;
using PokerCoach.HandHistories;

namespace PokerCoach.Infrastructure.Import;

/// <summary>
/// Stored form of a hand's full content (column <c>poker.hands.details</c>). Owned by persistence, not by
/// the parser: the parser's records can evolve without rewriting stored hands. Bump
/// <see cref="CurrentVersion"/> on any breaking change and keep readers able to read older versions.
/// </summary>
internal sealed record HandDetailsDocument(
    int Version,
    string TournamentName,
    decimal BuyInExcludingFee,
    decimal Fee,
    string Currency,
    bool IsRealMoney,
    string? HeroName,
    IReadOnlyList<HandDetailsDocument.Seat> Seats,
    IReadOnlyList<HandDetailsDocument.PlayerAction> Actions,
    IReadOnlyList<string> Board,
    IReadOnlyList<HandDetailsDocument.Shown> ShownCards,
    IReadOnlyList<HandDetailsDocument.Collection> Collections)
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public sealed record Seat(int Number, string Player, long Stack, decimal? Bounty);

    public sealed record PlayerAction(int Sequence, Street Street, string Player, ParsedActionType Type, long Amount, long? RaiseTo, bool IsAllIn);

    public sealed record Shown(string Player, IReadOnlyList<string> Cards, string? Description);

    public sealed record Collection(string Player, long Amount, PotKind Pot, int? SidePotNumber);

    public static HandDetailsDocument From(ParsedHand hand) => new(
        CurrentVersion,
        hand.TournamentName,
        hand.BuyIn.AmountExcludingFee,
        hand.BuyIn.Fee,
        hand.BuyIn.Currency,
        hand.IsRealMoney,
        hand.HeroName,
        hand.Seats.Select(s => new Seat(s.SeatNumber, s.PlayerName, s.Stack, s.Bounty)).ToList(),
        hand.Actions.Select(a => new PlayerAction(a.Sequence, a.Street, a.PlayerName, a.Type, a.Amount, a.RaiseTo, a.IsAllIn)).ToList(),
        Cards(hand.Board),
        hand.ShownCards.Select(s => new Shown(s.PlayerName, Cards(s.Cards), s.HandDescription)).ToList(),
        hand.Collections.Select(c => new Collection(c.PlayerName, c.Amount, c.Kind, c.SidePotNumber)).ToList());

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static HandDetailsDocument? FromJson(string json) => JsonSerializer.Deserialize<HandDetailsDocument>(json, JsonOptions);

    private static List<string> Cards(IEnumerable<Card> cards) => cards.Select(c => c.ToString()).ToList();
}
