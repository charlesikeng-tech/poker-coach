using PokerCoach.Domain.Poker;
using PokerCoach.HandHistories.Betclic;

namespace PokerCoach.HandHistories.Pmu;

public sealed class PmuHandHistoryParser : IHandHistoryParser
{
    public PokerRoom Room => PokerRoom.Pmu;

    public HandHistoryParseResult Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var blocks = new List<List<SourceLine>>();
        List<SourceLine>? current = null;
        var lines = PmuValues.SplitLines(content);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = new SourceLine(index + 1, lines[index]);
            if (line.Text.StartsWith(PmuValues.GamePrefix, StringComparison.Ordinal))
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
            var (hand, error) = new PmuHandReader(block).Read();
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
