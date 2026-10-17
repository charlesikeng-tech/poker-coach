using System.Text.RegularExpressions;
using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories.Winamax;

/// <summary>
/// Reads one Winamax cash game hand. All euro amounts are converted to integer cents for storage,
/// consistent with <see cref="ParsedHand"/>: amounts are in the smallest currency unit.
/// <para>
/// TotalPot is the gross pot (rake included), matching the ParsedHand contract:
/// TotalPot = Actions.Sum() = Collections.Sum() + Rake.
/// The file's "Total pot" line shows the net pot; rake is added back here.
/// </para>
/// </summary>
internal sealed class WinamaxCashGameHandReader(IReadOnlyList<SourceLine> block)
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
    private readonly Dictionary<string, long> streetContributions = new(StringComparer.Ordinal);

    private List<string> playerNamesLongestFirst = [];
    private Section section = Section.Table;
    private Street street = Street.Preflop;
    private long currentBet;
    private string? handId;
    private CashHeaderInfo? header;
    private string? tableName;
    private int maxSeats;
    private int buttonSeat;
    private string? heroName;
    private List<Card> heroCards = [];
    private List<Card>? summaryBoard;
    private long? netPot;
    private long rake;
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
        if (section != Section.Summary || header is null || tableName is null || netPot is not { } net)
        {
            Fail(ParseErrorCodes.IncompleteHand, lastLine);
            return (null, error);
        }

        var grossPot = net + rake;
        if (!Validate(net, grossPot, lastLine))
        {
            return (null, error);
        }

        var hand = new ParsedHand
        {
            Room = PokerRoom.Winamax,
            GameType = GameType.CashGame,
            ExternalHandId = header.HandId,
            TournamentName = null,
            ExternalTournamentId = null,
            BuyIn = null,
            TableName = tableName,
            MaxSeats = maxSeats,
            IsRealMoney = true,
            Level = 0,
            Ante = null,
            SmallBlind = header.SmallBlind,
            BigBlind = header.BigBlind,
            StartedAt = header.StartedAt,
            ButtonSeat = buttonSeat,
            Seats = seats,
            HeroName = heroName,
            HeroCards = heroCards,
            Actions = actions,
            Board = board,
            ShownCards = shownCards,
            Collections = collections,
            TotalPot = grossPot,
            Rake = rake,
            Stakes = header.Stakes,
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

        var match = WinamaxPatterns.CashHandHeader().Match(line.Text);
        if (!match.Success)
        {
            return Fail(ParseErrorCodes.UnrecognizedLine, line);
        }

        var id = match.Groups["id"].Value;
        handId = id;

        if (!WinamaxValues.TryParseEuros(match.Groups["sb"].Value, out var sbEuros)
            || !WinamaxValues.TryParseEuros(match.Groups["bb"].Value, out var bbEuros)
            || !WinamaxValues.TryParseUtc(match.Groups["date"].Value, out var startedAt))
        {
            return Fail(ParseErrorCodes.InvalidNumber, line);
        }

        var sb = EurosToCents(sbEuros);
        var bb = EurosToCents(bbEuros);
        if (sb <= 0 || bb <= 0)
        {
            return Fail(ParseErrorCodes.InvalidNumber, line);
        }

        // Stakes display: strip trailing zeros after decimal point.
        var sbDisplay = sbEuros.ToString("0.##");
        var bbDisplay = bbEuros.ToString("0.##");

        header = new CashHeaderInfo(id, sb, bb, $"{sbDisplay}/{bbDisplay}", startedAt);
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

        return ReadCollectionOrShow(player, remainder, line);
    }

    private bool ReadTable(SourceLine line)
    {
        var match = WinamaxPatterns.TableLine().Match(line.Text);
        if (!match.Success)
        {
            return Fail(ParseErrorCodes.UnrecognizedLine, line);
        }

        if (!WinamaxValues.TryParseInt(match.Groups["maxSeats"].Value, out maxSeats)
            || !WinamaxValues.TryParseInt(match.Groups["button"].Value, out buttonSeat))
        {
            return Fail(ParseErrorCodes.InvalidNumber, line);
        }

        tableName = match.Groups["table"].Value;
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

            playerNamesLongestFirst = seats.Select(s => s.PlayerName).OrderByDescending(n => n.Length).ToList();
            return Enter(Section.Blinds);
        }

        var match = WinamaxPatterns.CashSeatLine().Match(line.Text);
        if (!match.Success)
        {
            return Fail(ParseErrorCodes.UnrecognizedLine, line);
        }

        if (!WinamaxValues.TryParseInt(match.Groups["seat"].Value, out var seatNumber)
            || !WinamaxValues.TryParseEuros(match.Groups["stack"].Value, out var stackEuros))
        {
            return Fail(ParseErrorCodes.InvalidNumber, line);
        }

        var stack = EurosToCents(stackEuros);
        var name = match.Groups["name"].Value;
        if (streetContributions.ContainsKey(name) || seats.Exists(s => s.SeatNumber == seatNumber))
        {
            return Fail(ParseErrorCodes.DuplicatePlayer, line);
        }

        seats.Add(new ParsedSeat(seatNumber, name, stack, null));
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
        var match = WinamaxPatterns.CashPost().Match(remainder);
        if (!match.Success)
        {
            return Fail(ParseErrorCodes.UnrecognizedLine, line);
        }

        if (!WinamaxValues.TryParseEuros(match.Groups["amount"].Value, out var amountEuros))
        {
            return Fail(ParseErrorCodes.InvalidNumber, line);
        }

        var amount = EurosToCents(amountEuros);
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

        var callOrBet = WinamaxPatterns.CashCallOrBet().Match(remainder);
        if (callOrBet.Success)
        {
            if (!WinamaxValues.TryParseEuros(callOrBet.Groups["amount"].Value, out var amountEuros))
            {
                return Reject(ParseErrorCodes.InvalidNumber, line);
            }

            var amount = EurosToCents(amountEuros);
            var type = callOrBet.Groups["kind"].Value == "calls" ? ParsedActionType.Call : ParsedActionType.Bet;
            AddToStreet(player, amount);
            AddAction(player, type, amount, raiseTo: null, callOrBet.Groups["allIn"].Success);
            return LineResult.Accepted;
        }

        var raise = WinamaxPatterns.CashRaise().Match(remainder);
        if (raise.Success)
        {
            if (!WinamaxValues.TryParseEuros(raise.Groups["by"].Value, out var byEuros)
                || !WinamaxValues.TryParseEuros(raise.Groups["to"].Value, out var toEuros))
            {
                return Reject(ParseErrorCodes.InvalidNumber, line);
            }

            var raiseBy = EurosToCents(byEuros);
            var raiseTo = EurosToCents(toEuros);
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
        var collected = WinamaxPatterns.CashCollected().Match(remainder);
        if (collected.Success)
        {
            if (!WinamaxValues.TryParseEuros(collected.Groups["amount"].Value, out var amountEuros))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            var amount = EurosToCents(amountEuros);
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
        var total = WinamaxPatterns.CashTotalPot().Match(line.Text);
        if (total.Success)
        {
            if (netPot is not null)
            {
                return Fail(ParseErrorCodes.UnrecognizedLine, line);
            }

            if (!WinamaxValues.TryParseEuros(total.Groups["amount"].Value, out var netEuros))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            netPot = EurosToCents(netEuros);

            var rakeGroup = total.Groups["rake"];
            if (rakeGroup.Success)
            {
                if (!WinamaxValues.TryParseEuros(rakeGroup.Value, out var rakeEuros))
                {
                    return Fail(ParseErrorCodes.InvalidNumber, line);
                }

                rake = EurosToCents(rakeEuros);
            }

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

        return WinamaxPatterns.SummarySeat().IsMatch(line.Text) || Fail(ParseErrorCodes.UnrecognizedLine, line);
    }

    private bool Validate(long net, long gross, SourceLine lastLine)
    {
        var invested = actions.Sum(a => a.Amount);
        var collectedTotal = collections.Sum(c => c.Amount);

        if (invested != gross || collectedTotal != net)
        {
            return Fail(ParseErrorCodes.PotMismatch, lastLine);
        }

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

    private static long EurosToCents(decimal euros) => (long)Math.Round(euros * 100m, MidpointRounding.AwayFromZero);

    private sealed record CashHeaderInfo(string HandId, long SmallBlind, long BigBlind, string Stakes, DateTimeOffset StartedAt);
}
