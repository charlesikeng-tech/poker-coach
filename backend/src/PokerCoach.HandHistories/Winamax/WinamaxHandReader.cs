using System.Text.RegularExpressions;
using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories.Winamax;

internal readonly record struct SourceLine(int Number, string Text);

/// <summary>
/// Reads one hand (header line to last summary line). Single use: create one reader per hand.
/// Every rule implemented here was checked against real files first (docs/research/winamax-format-notes.md).
/// </summary>
internal sealed class WinamaxHandReader(IReadOnlyList<SourceLine> block)
{
    internal const int MaxLineLength = 1_000;

    private enum Section
    {
        Table,
        Seats,
        Blinds,
        Actions,
        Showdown,
        Summary,
    }

    private enum LineResult
    {
        NotMatched,
        Accepted,
        Rejected,
    }

    private readonly List<ParsedSeat> seats = [];
    private readonly List<ParsedAction> actions = [];
    private readonly List<Card> board = [];
    private readonly List<ParsedShownCards> shownCards = [];
    private readonly List<ParsedPotCollection> collections = [];

    // Chips each player has put in on the current street (blinds count pre-flop, antes do not).
    private readonly Dictionary<string, long> streetContributions = new(StringComparer.Ordinal);

    private List<string> playerNamesLongestFirst = [];
    private Section section = Section.Table;
    private Street street = Street.Preflop;
    private long currentBet;
    private string? handId;
    private HeaderInfo? header;
    private TableInfo? table;
    private string? heroName;
    private List<Card> heroCards = [];
    private List<Card>? summaryBoard;
    private long? totalPot;
    private ParseError? error;

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
        if (section != Section.Summary || header is null || table is null || totalPot is not { } pot)
        {
            Fail(ParseErrorCodes.IncompleteHand, lastLine);
            return (null, error);
        }

        if (!Validate(pot, lastLine))
        {
            return (null, error);
        }

        var hand = new ParsedHand
        {
            Room = PokerRoom.Winamax,
            ExternalHandId = header.HandId,
            TournamentName = header.TournamentName,
            ExternalTournamentId = table.TournamentId,
            BuyIn = header.BuyIn,
            TableName = table.Name,
            MaxSeats = table.MaxSeats,
            IsRealMoney = table.IsRealMoney,
            Level = header.Level,
            Ante = header.Ante,
            SmallBlind = header.SmallBlind,
            BigBlind = header.BigBlind,
            StartedAt = header.StartedAt,
            ButtonSeat = table.ButtonSeat,
            Seats = seats,
            HeroName = heroName,
            HeroCards = heroCards,
            Actions = actions,
            Board = board,
            ShownCards = shownCards,
            Collections = collections,
            TotalPot = pot,
            LineNumber = block[0].Number,
        };
        return (hand, null);
    }

    private bool ReadHeader(SourceLine line)
    {
        if (line.Text.Length > MaxLineLength)
        {
            return Fail(ParseErrorCodes.LineTooLong, line);
        }

        var match = WinamaxPatterns.HandHeader().Match(line.Text);
        if (!match.Success)
        {
            return Fail(ParseErrorCodes.UnrecognizedLine, line);
        }

        var id = match.Groups["id"].Value;
        handId = id;

        var buyIn = WinamaxPatterns.HandBuyIn().Match(match.Groups["buyIn"].Value);
        if (!buyIn.Success)
        {
            return Fail(ParseErrorCodes.UnsupportedBuyIn, line);
        }

        long? ante = null;
        var anteGroup = match.Groups["ante"];
        if (anteGroup.Success)
        {
            if (!WinamaxValues.TryParseChips(anteGroup.Value, out var anteValue))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            ante = anteValue;
        }

        if (!WinamaxValues.TryParseEuros(buyIn.Groups["amount"].Value, out var amountExcludingFee)
            || !WinamaxValues.TryParseEuros(buyIn.Groups["fee"].Value, out var fee)
            || !WinamaxValues.TryParseInt(match.Groups["level"].Value, out var level)
            || !WinamaxValues.TryParseChips(match.Groups["sb"].Value, out var smallBlind)
            || !WinamaxValues.TryParseChips(match.Groups["bb"].Value, out var bigBlind)
            || !WinamaxValues.TryParseUtc(match.Groups["date"].Value, out var startedAt))
        {
            return Fail(ParseErrorCodes.InvalidNumber, line);
        }

        header = new HeaderInfo(
            id,
            match.Groups["name"].Value,
            new HandBuyIn(amountExcludingFee, fee, WinamaxValues.Currency),
            level,
            ante,
            smallBlind,
            bigBlind,
            startedAt);
        return true;
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

        // Collections and shown cards: after the last action, or in the showdown. A winner may show
        // voluntarily without a showdown section.
        return ReadCollectionOrShow(player, remainder, line);
    }

    private bool ReadTable(SourceLine line)
    {
        var match = WinamaxPatterns.TableLine().Match(line.Text);
        if (!match.Success)
        {
            return Fail(ParseErrorCodes.UnrecognizedLine, line);
        }

        if (!WinamaxValues.TryParseInt(match.Groups["maxSeats"].Value, out var maxSeats)
            || !WinamaxValues.TryParseInt(match.Groups["button"].Value, out var buttonSeat))
        {
            return Fail(ParseErrorCodes.InvalidNumber, line);
        }

        var tableName = match.Groups["table"].Value;
        var tournamentId = WinamaxPatterns.TournamentIdInTableName().Match(tableName);
        table = new TableInfo(
            tableName,
            tournamentId.Success ? tournamentId.Groups["id"].Value : null,
            maxSeats,
            buttonSeat,
            match.Groups["money"].Value == "real money");
        return Enter(Section.Seats);
    }

    private bool ReadSeatOrBlindsMarker(SourceLine line)
    {
        if (line.Text == "*** ANTE/BLINDS ***")
        {
            if (seats.Count == 0)
            {
                return Fail(ParseErrorCodes.MissingSeats, line);
            }

            // Longest first, so that "Bob 2" is never read as "Bob" followed by "2 folds".
            playerNamesLongestFirst = seats.Select(s => s.PlayerName).OrderByDescending(n => n.Length).ToList();
            return Enter(Section.Blinds);
        }

        var match = WinamaxPatterns.SeatLine().Match(line.Text);
        if (!match.Success)
        {
            return Fail(ParseErrorCodes.UnrecognizedLine, line);
        }

        if (!WinamaxValues.TryParseInt(match.Groups["seat"].Value, out var seatNumber)
            || !WinamaxValues.TryParseChips(match.Groups["stack"].Value, out var stack))
        {
            return Fail(ParseErrorCodes.InvalidNumber, line);
        }

        decimal? bounty = null;
        var bountyGroup = match.Groups["bounty"];
        if (bountyGroup.Success)
        {
            if (!WinamaxValues.TryParseEuros(bountyGroup.Value, out var bountyValue))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            bounty = bountyValue;
        }

        var name = match.Groups["name"].Value;
        if (streetContributions.ContainsKey(name) || seats.Exists(s => s.SeatNumber == seatNumber))
        {
            return Fail(ParseErrorCodes.DuplicatePlayer, line);
        }

        seats.Add(new ParsedSeat(seatNumber, name, stack, bounty));
        streetContributions[name] = 0;
        return true;
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
            // "<name> [c1 c2]"
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
        var match = WinamaxPatterns.Post().Match(remainder);
        if (!match.Success)
        {
            return Fail(ParseErrorCodes.UnrecognizedLine, line);
        }

        if (!WinamaxValues.TryParseChips(match.Groups["amount"].Value, out var amount))
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

        var callOrBet = WinamaxPatterns.CallOrBet().Match(remainder);
        if (callOrBet.Success)
        {
            if (!WinamaxValues.TryParseChips(callOrBet.Groups["amount"].Value, out var amount))
            {
                return Reject(ParseErrorCodes.InvalidNumber, line);
            }

            var type = callOrBet.Groups["kind"].Value == "calls" ? ParsedActionType.Call : ParsedActionType.Bet;
            AddToStreet(player, amount);
            AddAction(player, type, amount, raiseTo: null, callOrBet.Groups["allIn"].Success);
            return LineResult.Accepted;
        }

        var raise = WinamaxPatterns.Raise().Match(remainder);
        if (raise.Success)
        {
            if (!WinamaxValues.TryParseChips(raise.Groups["by"].Value, out var raiseBy)
                || !WinamaxValues.TryParseChips(raise.Groups["to"].Value, out var raiseTo))
            {
                return Reject(ParseErrorCodes.InvalidNumber, line);
            }

            // "raises X to Y": Y is the player's street total and X the raise over the current bet.
            // Both must agree with the bets seen so far, otherwise the hand is not what we think it is.
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
        var collected = WinamaxPatterns.Collected().Match(remainder);
        if (collected.Success)
        {
            if (!WinamaxValues.TryParseChips(collected.Groups["amount"].Value, out var amount))
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
        var total = WinamaxPatterns.TotalPot().Match(line.Text);
        if (total.Success)
        {
            if (totalPot is not null)
            {
                return Fail(ParseErrorCodes.UnrecognizedLine, line);
            }

            if (!WinamaxValues.TryParseChips(total.Groups["amount"].Value, out var amount))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            totalPot = amount;
            return true;
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

        // Per-seat result lines repeat what the hand already proved (collections, shown cards).
        return WinamaxPatterns.SummarySeat().IsMatch(line.Text) || Fail(ParseErrorCodes.UnrecognizedLine, line);
    }

    private bool Validate(long pot, SourceLine lastLine)
    {
        long invested = 0;
        foreach (var action in actions)
        {
            invested += action.Amount;
        }

        long collectedTotal = 0;
        foreach (var collection in collections)
        {
            collectedTotal += collection.Amount;
        }

        if (invested != pot || collectedTotal != pot)
        {
            return Fail(ParseErrorCodes.PotMismatch, lastLine);
        }

        // No "Board:" line is printed when the hand ends before the flop.
        var boardMatches = summaryBoard is null ? board.Count == 0 : summaryBoard.SequenceEqual(board);
        return boardMatches || Fail(ParseErrorCodes.BoardMismatch, lastLine);
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

    private bool Enter(Section next)
    {
        section = next;
        return true;
    }

    private bool Fail(string code, SourceLine line)
    {
        error ??= new ParseError(code, line.Number, handId);
        return false;
    }

    private LineResult Reject(string code, SourceLine line)
    {
        Fail(code, line);
        return LineResult.Rejected;
    }

    private sealed record HeaderInfo(
        string HandId,
        string TournamentName,
        HandBuyIn BuyIn,
        int Level,
        long? Ante,
        long SmallBlind,
        long BigBlind,
        DateTimeOffset StartedAt);

    private sealed record TableInfo(string Name, string? TournamentId, int MaxSeats, int ButtonSeat, bool IsRealMoney);
}
