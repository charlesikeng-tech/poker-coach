using System.Text.RegularExpressions;
using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories.Winamax;

internal readonly record struct SourceLine(int Number, string Text);

/// <summary>
/// Shared parsing pipeline for Winamax hand histories. Tournament and cash game readers differ only in
/// header parsing, seat/table structure, amount format (integer chips vs euro decimals→cents) and the
/// regex patterns used for amounts. This base class owns the section state machine, the street/action/
/// showdown/summary logic, and the validation contract; subclasses plug in the format-specific pieces.
/// </summary>
internal abstract class WinamaxHandReaderBase(IReadOnlyList<SourceLine> block)
{
    internal const int MaxLineLength = 1_000;

    protected enum Section
    {
        Table,
        Seats,
        Blinds,
        Actions,
        Showdown,
        Summary,
    }

    protected enum LineResult
    {
        NotMatched,
        Accepted,
        Rejected,
    }

    protected readonly List<ParsedSeat> seats = [];
    protected readonly List<ParsedAction> actions = [];
    protected readonly List<Card> board = [];
    protected readonly List<ParsedShownCards> shownCards = [];
    protected readonly List<ParsedPotCollection> collections = [];
    protected readonly Dictionary<string, long> streetContributions = new(StringComparer.Ordinal);

    private List<string> playerNamesLongestFirst = [];
    private Section section = Section.Table;
    private Street street = Street.Preflop;
    private long currentBet;
    protected string? handId;
    protected string? heroName;
    protected List<Card> heroCards = [];
    protected List<Card>? summaryBoard;
    protected ParseError? error;

    protected abstract bool TryParseAmount(string text, out long value);
    protected abstract Regex PostPattern { get; }
    protected abstract Regex CallOrBetPattern { get; }
    protected abstract Regex RaisePattern { get; }
    protected abstract Regex CollectedPattern { get; }

    protected int FirstLineNumber => block[0].Number;

    protected abstract bool ReadHeader(SourceLine line);
    protected abstract bool ReadTable(SourceLine line);
    protected abstract bool ReadSeat(SourceLine line);
    protected abstract LineResult ReadPotLine(SourceLine line);
    protected abstract bool IsReadComplete { get; }
    protected abstract ParsedHand? ValidateAndBuild(SourceLine lastLine);

    public (ParsedHand? Hand, ParseError? Error) Read()
    {
        if (!ReadHeader(block[0]))
        {
            return (null, error);
        }

        for (var i = 1; i < block.Count; i++)
        {
            if (!ReadLine(block[i]))
            {
                return (null, error);
            }
        }

        var lastLine = block[^1];
        if (section != Section.Summary || !IsReadComplete)
        {
            Fail(ParseErrorCodes.IncompleteHand, lastLine);
            return (null, error);
        }

        var hand = ValidateAndBuild(lastLine);
        return hand is not null ? (hand, null) : (null, error);
    }

    private bool ReadLine(SourceLine line)
    {
        var text = line.Text;
        if (text.Length > MaxLineLength)
        {
            return Fail(ParseErrorCodes.LineTooLong, line);
        }

        switch (section)
        {
            case Section.Table:
                return ReadTable(line);
            case Section.Seats:
                return ReadSeatOrBlindsMarker(line);
            case Section.Summary:
                return ReadSummaryLine(line);
        }

        if (text == "*** PRE-FLOP ***")
        {
            return section == Section.Blinds ? Enter(Section.Actions) : Fail(ParseErrorCodes.UnexpectedSection, line);
        }

        if (text == "*** SHOW DOWN ***")
        {
            return section == Section.Actions ? Enter(Section.Showdown) : Fail(ParseErrorCodes.UnexpectedSection, line);
        }

        if (text == "*** SUMMARY ***")
        {
            return section is Section.Actions or Section.Showdown
                ? Enter(Section.Summary)
                : Fail(ParseErrorCodes.UnexpectedSection, line);
        }

        var streetMarker = WinamaxPatterns.StreetMarker().Match(text);
        if (streetMarker.Success)
        {
            return section == Section.Actions ? ReadStreet(streetMarker, line) : Fail(ParseErrorCodes.UnexpectedSection, line);
        }

        if (section == Section.Blinds && text.StartsWith("Dealt to ", StringComparison.Ordinal))
        {
            return ReadDealtTo(line);
        }

        var (player, remainder) = SplitPlayer(text);
        if (player is null)
        {
            return Fail(ParseErrorCodes.UnrecognizedLine, line);
        }

        if (section == Section.Blinds)
        {
            return ReadPost(player, remainder, line);
        }

        if (section == Section.Actions)
        {
            var result = ReadBettingAction(player, remainder, line);
            if (result != LineResult.NotMatched)
            {
                return result == LineResult.Accepted;
            }
        }

        return ReadCollectionOrShow(player, remainder, line);
    }

    private bool ReadSeatOrBlindsMarker(SourceLine line)
    {
        if (line.Text == "*** ANTE/BLINDS ***")
        {
            if (seats.Count == 0)
            {
                return Fail(ParseErrorCodes.MissingSeats, line);
            }

            playerNamesLongestFirst = seats.Select(s => s.PlayerName).OrderByDescending(n => n.Length).ToList();
            return Enter(Section.Blinds);
        }

        return ReadSeat(line);
    }

    private bool ReadStreet(Match match, SourceLine line)
    {
        var (nextStreet, newCards, cardsBefore) = match.Groups["street"].Value switch
        {
            "FLOP" => (Street.Flop, 3, 0),
            "TURN" => (Street.Turn, 1, 3),
            _ => (Street.River, 1, 4),
        };

        if (!WinamaxValues.TryParseCards(match.Groups["cards"].Value, out var cards))
        {
            return Fail(ParseErrorCodes.InvalidCard, line);
        }

        if (cards.Count != newCards || board.Count != cardsBefore)
        {
            return Fail(ParseErrorCodes.InvalidBoard, line);
        }

        board.AddRange(cards);
        street = nextStreet;
        currentBet = 0;
        foreach (var name in playerNamesLongestFirst)
        {
            streetContributions[name] = 0;
        }

        return true;
    }

    private bool ReadDealtTo(SourceLine line)
    {
        var rest = line.Text["Dealt to ".Length..];
        foreach (var name in playerNamesLongestFirst)
        {
            if (rest.Length > name.Length + 3
                && rest.StartsWith(name, StringComparison.Ordinal)
                && rest[name.Length] == ' '
                && rest[name.Length + 1] == '['
                && rest[^1] == ']')
            {
                if (heroName is not null)
                {
                    return Fail(ParseErrorCodes.InvalidHoleCards, line);
                }

                if (!WinamaxValues.TryParseCards(rest[(name.Length + 2)..^1], out var cards))
                {
                    return Fail(ParseErrorCodes.InvalidCard, line);
                }

                if (cards.Count != 2)
                {
                    return Fail(ParseErrorCodes.InvalidHoleCards, line);
                }

                heroName = name;
                heroCards = cards;
                return true;
            }
        }

        return Fail(ParseErrorCodes.UnknownPlayer, line);
    }

    private bool ReadPost(string player, string remainder, SourceLine line)
    {
        var match = PostPattern.Match(remainder);
        if (!match.Success)
        {
            return Fail(ParseErrorCodes.UnrecognizedLine, line);
        }

        if (!TryParseAmount(match.Groups["amount"].Value, out var amount))
        {
            return Fail(ParseErrorCodes.InvalidNumber, line);
        }

        var type = match.Groups["kind"].Value switch
        {
            "ante" => ParsedActionType.PostAnte,
            "small blind" => ParsedActionType.PostSmallBlind,
            _ => ParsedActionType.PostBigBlind,
        };

        if (type != ParsedActionType.PostAnte)
        {
            AddToStreet(player, amount);
        }

        AddAction(player, type, amount, raiseTo: null, match.Groups["allIn"].Success);
        return true;
    }

    private LineResult ReadBettingAction(string player, string remainder, SourceLine line)
    {
        if (remainder == "folds")
        {
            AddAction(player, ParsedActionType.Fold, 0, raiseTo: null, isAllIn: false);
            return LineResult.Accepted;
        }

        if (remainder == "checks")
        {
            AddAction(player, ParsedActionType.Check, 0, raiseTo: null, isAllIn: false);
            return LineResult.Accepted;
        }

        var callOrBet = CallOrBetPattern.Match(remainder);
        if (callOrBet.Success)
        {
            if (!TryParseAmount(callOrBet.Groups["amount"].Value, out var amount))
            {
                return Reject(ParseErrorCodes.InvalidNumber, line);
            }

            var type = callOrBet.Groups["kind"].Value == "calls" ? ParsedActionType.Call : ParsedActionType.Bet;
            AddToStreet(player, amount);
            AddAction(player, type, amount, raiseTo: null, callOrBet.Groups["allIn"].Success);
            return LineResult.Accepted;
        }

        var raise = RaisePattern.Match(remainder);
        if (raise.Success)
        {
            if (!TryParseAmount(raise.Groups["by"].Value, out var raiseBy)
                || !TryParseAmount(raise.Groups["to"].Value, out var raiseTo))
            {
                return Reject(ParseErrorCodes.InvalidNumber, line);
            }

            var alreadyIn = streetContributions[player];
            if (raiseTo - currentBet != raiseBy || raiseTo <= alreadyIn)
            {
                return Reject(ParseErrorCodes.InconsistentRaise, line);
            }

            AddAction(player, ParsedActionType.Raise, raiseTo - alreadyIn, raiseTo, raise.Groups["allIn"].Success);
            streetContributions[player] = raiseTo;
            currentBet = raiseTo;
            return LineResult.Accepted;
        }

        return LineResult.NotMatched;
    }

    private bool ReadCollectionOrShow(string player, string remainder, SourceLine line)
    {
        var collected = CollectedPattern.Match(remainder);
        if (collected.Success)
        {
            if (!TryParseAmount(collected.Groups["amount"].Value, out var amount))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            var kind = PotKind.Single;
            int? sidePotNumber = null;
            if (collected.Groups["main"].Success)
            {
                kind = PotKind.Main;
            }
            else if (collected.Groups["side"].Success)
            {
                if (!WinamaxValues.TryParseInt(collected.Groups["side"].Value, out var number))
                {
                    return Fail(ParseErrorCodes.InvalidNumber, line);
                }

                kind = PotKind.Side;
                sidePotNumber = number;
            }

            collections.Add(new ParsedPotCollection(player, amount, kind, sidePotNumber));
            return true;
        }

        var shows = WinamaxPatterns.Shows().Match(remainder);
        if (shows.Success)
        {
            if (!WinamaxValues.TryParseCards(shows.Groups["cards"].Value, out var cards))
            {
                return Fail(ParseErrorCodes.InvalidCard, line);
            }

            if (cards.Count != 2)
            {
                return Fail(ParseErrorCodes.InvalidHoleCards, line);
            }

            var description = shows.Groups["description"];
            shownCards.Add(new ParsedShownCards(player, cards, description.Success ? description.Value : null));
            return true;
        }

        return Fail(ParseErrorCodes.UnrecognizedLine, line);
    }

    private bool ReadSummaryLine(SourceLine line)
    {
        var potResult = ReadPotLine(line);
        if (potResult != LineResult.NotMatched)
        {
            return potResult == LineResult.Accepted;
        }

        var summaryBoardMatch = WinamaxPatterns.SummaryBoard().Match(line.Text);
        if (summaryBoardMatch.Success)
        {
            if (summaryBoard is not null)
            {
                return Fail(ParseErrorCodes.UnrecognizedLine, line);
            }

            if (!WinamaxValues.TryParseCards(summaryBoardMatch.Groups["cards"].Value, out var cards))
            {
                return Fail(ParseErrorCodes.InvalidCard, line);
            }

            summaryBoard = cards;
            return true;
        }

        return WinamaxPatterns.SummarySeat().IsMatch(line.Text) || Fail(ParseErrorCodes.UnrecognizedLine, line);
    }

    private (string? Player, string Remainder) SplitPlayer(string text)
    {
        foreach (var name in playerNamesLongestFirst)
        {
            if (text.Length > name.Length + 1
                && text[name.Length] == ' '
                && text.StartsWith(name, StringComparison.Ordinal))
            {
                return (name, text[(name.Length + 1)..]);
            }
        }

        return (null, string.Empty);
    }

    private void AddToStreet(string player, long amount)
    {
        var total = streetContributions[player] + amount;
        streetContributions[player] = total;
        currentBet = Math.Max(currentBet, total);
    }

    private void AddAction(string player, ParsedActionType type, long amount, long? raiseTo, bool isAllIn) =>
        actions.Add(new ParsedAction(actions.Count + 1, street, player, type, amount, raiseTo, isAllIn));

    protected bool Enter(Section next)
    {
        section = next;
        return true;
    }

    protected bool Fail(string code, SourceLine line)
    {
        error ??= new ParseError(code, line.Number, handId);
        return false;
    }

    protected LineResult Reject(string code, SourceLine line)
    {
        Fail(code, line);
        return LineResult.Rejected;
    }
}
