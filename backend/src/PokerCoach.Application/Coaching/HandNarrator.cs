using System.Globalization;
using System.Text;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Application.Coaching;

/// <summary>
/// Writes a hand as a short, anonymous story for the model: players are named by position only, amounts
/// are in big blinds, nothing identifies a person, a tournament or a room (ADR-0008, principle 3).
/// </summary>
public static class HandNarrator
{
    private static readonly Dictionary<PokerPosition, string> Labels = new()
    {
        [PokerPosition.Utg] = "UTG",
        [PokerPosition.Utg1] = "UTG+1",
        [PokerPosition.Utg2] = "UTG+2",
        [PokerPosition.Lojack] = "LJ",
        [PokerPosition.Hijack] = "HJ",
        [PokerPosition.Cutoff] = "CO",
        [PokerPosition.Button] = "BTN",
        [PokerPosition.SmallBlind] = "SB",
        [PokerPosition.BigBlind] = "BB",
    };

    public static string Label(PokerPosition position) => Labels[position];

    public static string Tell(ExampleHand example)
    {
        ArgumentNullException.ThrowIfNull(example);

        var hand = example.Hand;
        var positions = PositionResolver.Resolve(hand);
        var dealt = hand.Actions.Select(a => a.Player).ToHashSet(StringComparer.Ordinal);
        string Name(string player) =>
            player == hand.Hero
                ? $"Hero ({(positions.TryGetValue(player, out var hp) ? Labels[hp] : "?")})"
                : positions.TryGetValue(player, out var p) ? Labels[p] : "Villain";
        string Bb(long chips) => Math.Round((decimal)chips / hand.BigBlind, 1).ToString(CultureInfo.InvariantCulture) + " BB";

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"Level {example.Level}, {dealt.Count} players");
        if (example.Ante is > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $", ante {Bb(example.Ante.Value)}");
        }

        text.AppendLine(".");
        text.Append("Stacks: ");
        text.AppendJoin(", ", hand.Seats
            .Where(s => dealt.Contains(s.Player))
            .Select(s => $"{Name(s.Player)} {Bb(s.Stack)}"));
        text.AppendLine(".");
        if (example.HeroCards is { Length: > 0 } cards)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Hero holds {cards}.");
        }

        foreach (var street in new[] { Street.Preflop, Street.Flop, Street.Turn, Street.River })
        {
            var actions = hand.Actions
                .Where(a => a.Street == street && a.Kind is not (ActionKind.PostAnte or ActionKind.PostSmallBlind or ActionKind.PostBigBlind))
                .ToList();
            var boardCount = street switch { Street.Flop => 3, Street.Turn => 4, Street.River => 5, _ => 0 };
            if (example.Board.Count < boardCount)
            {
                // The hand ended before this street.
                continue;
            }

            text.Append(street switch { Street.Preflop => "Preflop", Street.Flop => "Flop", Street.Turn => "Turn", _ => "River" });
            if (boardCount > 0)
            {
                text.Append(" [").AppendJoin(' ', example.Board.Take(boardCount)).Append(']');
            }

            text.Append(": ");
            text.AppendJoin("; ", actions.Select(a => a.Kind switch
            {
                ActionKind.Fold => $"{Name(a.Player)} folds",
                ActionKind.Check => $"{Name(a.Player)} checks",
                ActionKind.Call => $"{Name(a.Player)} calls {Bb(a.Amount)}",
                ActionKind.Bet => $"{Name(a.Player)} bets {Bb(a.Amount)}",
                _ => $"{Name(a.Player)} raises by {Bb(a.Amount)}",
            } + (a.IsAllIn ? " (all-in)" : string.Empty)));
            text.AppendLine(".");
        }

        var won = hand.Collected.GetValueOrDefault(hand.Hero);
        var invested = hand.Actions.Where(a => a.Player == hand.Hero).Sum(a => a.Amount);
        text.Append(CultureInfo.InvariantCulture, $"Result for Hero: {(won - invested >= 0 ? "+" : string.Empty)}{Bb(won - invested)}.");
        return text.ToString();
    }
}
