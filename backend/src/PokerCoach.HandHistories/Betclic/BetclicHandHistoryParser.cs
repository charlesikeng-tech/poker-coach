using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories.Betclic;

public sealed class BetclicHandHistoryParser : IHandHistoryParser
{
    public PokerRoom Room => PokerRoom.Betclic;

    public HandHistoryParseResult Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var blocks = new List<List<SourceLine>>();
        List<SourceLine>? current = null;
        var lines = BetclicValues.SplitLines(content);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = new SourceLine(index + 1, lines[index]);
            if (line.Text == BetclicValues.HeaderMarker)
            {
                current = [line];
                blocks.Add(current);
            }
            else if (line.Text.Length == 0)
            {
                continue;
            }
            else if (current is null)
            {
                return new HandHistoryParseResult([], [new ParseError(ParseErrorCodes.UnrecognizedFormat, line.Number, null)]);
            }
            else
            {
                current.Add(line);
            }
        }

        if (blocks.Count == 0)
        {
            return new HandHistoryParseResult([], [new ParseError(ParseErrorCodes.EmptyFile, 1, null)]);
        }

        var hands = new List<ParsedHand>(blocks.Count);
        var errors = new List<ParseError>();
        foreach (var block in blocks)
        {
            var (hand, error) = new BetclicHandReader(block).Read();
            if (hand is not null)
            {
                hands.Add(hand);
            }
            else if (error is not null)
            {
                errors.Add(error);
            }
        }

        return new HandHistoryParseResult(hands, errors);
    }
}

internal readonly record struct SourceLine(int Number, string Text);
