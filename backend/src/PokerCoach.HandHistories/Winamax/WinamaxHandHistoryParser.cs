using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories.Winamax;

/// <summary>
/// Parses Winamax No-Limit Hold'em tournament hand-history files.
/// Strict on purpose: a hand is accepted only if every line is understood and its chips balance
/// (chips put in = total pot = chips collected). A rejected hand is reported, never partially kept;
/// the other hands of the file are still returned. Unknown lines (new provider features, chat…) are
/// therefore visible as UNRECOGNIZED_LINE errors instead of silently skewing statistics.
/// </summary>
public sealed class WinamaxHandHistoryParser : IHandHistoryParser
{
    public PokerRoom Room => PokerRoom.Winamax;

    public HandHistoryParseResult Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var blocks = new List<List<SourceLine>>();
        List<SourceLine>? current = null;
        var lines = WinamaxValues.SplitLines(content);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = new SourceLine(index + 1, lines[index]);
            if (line.Text.StartsWith(WinamaxValues.HandHeaderPrefix, StringComparison.Ordinal))
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
                // Content before the first hand header: not a Winamax hand history (or a summary file).
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
            var (hand, error) = new WinamaxHandReader(block).Read();
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
