using System.Globalization;
using PokerCoach.Domain.Poker;
using PokerCoach.HandHistories.Betclic;

namespace PokerCoach.HandHistories.Pmu;

internal sealed class PmuHandReader(IReadOnlyList<SourceLine> block)
{
    internal const int MaxLineLength = 1_000;

    private enum Section { Header, Seats, HoleCards, Actions, Summary }

    private readonly List<ParsedSeat> seats = [];
    private readonly List<ParsedAction> actions = [];
    private readonly List<Card> board = [];
    private readonly List<ParsedShownCards> shownCards = [];
    private readonly List<ParsedPotCollection> collections = [];
    private readonly Dictionary<string, long> streetContributions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> remainingStacks = new(StringComparer.Ordinal);

    private Section section = Section.Header;
    private Street street = Street.Preflop;
    private long currentBet;
    private string? heroName;
    private List<Card> heroCards = [];
    private int buttonSeat;
    private string handId = string.Empty;
    private string tableName = string.Empty;
    private int maxSeats;
    private long smallBlind;
    private long bigBlind;
    private string stakes = string.Empty;
    private DateTimeOffset startedAt;
    private long totalPot;
    private long rake;
    private ParseError? error;

    public (ParsedHand? Hand, ParseError? Error) Read()
    {
        for (var i = 0; i < block.Count; i++)
        {
            if (!ReadLine(block[i]))
            {
                return (null, error);
            }
        }

        if (section != Section.Summary || handId.Length == 0)
        {
            Fail(ParseErrorCodes.IncompleteHand, block[^1]);
            return (null, error);
        }

        if (seats.Count == 0)
        {
            Fail(ParseErrorCodes.MissingSeats, block[^1]);
            return (null, error);
        }

        if (!Validate(block[^1]))
        {
            return (null, error);
        }

        NormalizeUncalledBetReturn();

        var hand = new ParsedHand
        {
            Room = PokerRoom.Pmu,
            GameType = GameType.CashGame,
            ExternalHandId = handId,
            TournamentName = null,
            ExternalTournamentId = null,
            BuyIn = null,
            TableName = tableName,
            MaxSeats = maxSeats,
            IsRealMoney = true,
            Level = 0,
            Ante = null,
            SmallBlind = smallBlind,
            BigBlind = bigBlind,
            StartedAt = startedAt,
            ButtonSeat = buttonSeat,
            Seats = seats,
            HeroName = heroName,
            HeroCards = heroCards,
            Actions = actions,
            Board = board,
            ShownCards = shownCards,
            Collections = collections,
            TotalPot = totalPot,
            Rake = rake,
            Stakes = stakes,
            LineNumber = block[0].Number,
        };
        return (hand, null);
    }

    private bool ReadLine(SourceLine line)
    {
        var text = line.Text;
        if (text.Length > MaxLineLength)
        {
            return Fail(ParseErrorCodes.LineTooLong, line);
        }

        if (text == PmuValues.HoleCardsMarker)
        {
            if (section != Section.Seats || seats.Count == 0)
            {
                return Fail(ParseErrorCodes.UnexpectedSection, line);
            }

            section = Section.HoleCards;
            return true;
        }

        if (text == PmuValues.SummaryMarker)
        {
            if (section is not (Section.Actions or Section.HoleCards))
            {
                return Fail(ParseErrorCodes.UnexpectedSection, line);
            }

            section = Section.Summary;
            return true;
        }

        var streetMatch = PmuPatterns.StreetMarker().Match(text);
        if (streetMatch.Success)
        {
            return section == Section.Actions ? ReadStreet(streetMatch, line) : Fail(ParseErrorCodes.UnexpectedSection, line);
        }

        return section switch
        {
            Section.Header => ReadHeader(line),
            Section.Seats => ReadSeatOrPost(line),
            Section.HoleCards => ReadDealtOrFirstAction(line),
            Section.Actions => ReadAction(line),
            Section.Summary => ReadSummaryLine(line),
            _ => Fail(ParseErrorCodes.UnrecognizedLine, line),
        };
    }

    private bool ReadHeader(SourceLine line)
    {
        var text = line.Text;

        var headerMatch = PmuPatterns.HeaderLine().Match(text);
        if (headerMatch.Success)
        {
            handId = headerMatch.Groups["id"].Value;
            if (!PmuValues.TryParseCents(headerMatch.Groups["sb"].Value, out smallBlind)
                || !PmuValues.TryParseCents(headerMatch.Groups["bb"].Value, out bigBlind))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            stakes = $"{headerMatch.Groups["sb"].Value}/{headerMatch.Groups["bb"].Value}";
            if (!PmuValues.TryParseDateTime(headerMatch.Groups["date"].Value, headerMatch.Groups["tz"].Value, out startedAt))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            return true;
        }

        var tableInfo = PmuPatterns.TableInfoLine().Match(text);
        if (tableInfo.Success)
        {
            if (!int.TryParse(tableInfo.Groups["size"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out maxSeats))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            return true;
        }

        var table = PmuPatterns.TableLine().Match(text);
        if (table.Success)
        {
            tableName = table.Groups["id"].Value;
            return true;
        }

        // First seat line transitions to Seats section.
        var seat = PmuPatterns.SeatLine().Match(text);
        if (seat.Success)
        {
            section = Section.Seats;
            return AddSeat(seat, line);
        }

        return Fail(ParseErrorCodes.UnrecognizedLine, line);
    }

    private bool ReadSeatOrPost(SourceLine line)
    {
        var seat = PmuPatterns.SeatLine().Match(line.Text);
        if (seat.Success)
        {
            return AddSeat(seat, line);
        }

        // Post actions appear after seats, before HOLE CARDS.
        var post = PmuPatterns.Post().Match(line.Text);
        if (post.Success)
        {
            return ReadPost(post, line);
        }

        return Fail(ParseErrorCodes.UnrecognizedLine, line);
    }

    private bool AddSeat(System.Text.RegularExpressions.Match match, SourceLine line)
    {
        if (!int.TryParse(match.Groups["seat"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seatNumber)
            || !PmuValues.TryParseCents(match.Groups["stack"].Value, out var stack))
        {
            return Fail(ParseErrorCodes.InvalidNumber, line);
        }

        var name = match.Groups["name"].Value;
        if (streetContributions.ContainsKey(name) || seats.Exists(s => s.SeatNumber == seatNumber))
        {
            return Fail(ParseErrorCodes.DuplicatePlayer, line);
        }

        if (match.Groups["dealer"].Success)
        {
            buttonSeat = seatNumber;
        }

        seats.Add(new ParsedSeat(seatNumber, name, stack, null));
        streetContributions[name] = 0;
        remainingStacks[name] = stack;
        return true;
    }

    private bool ReadPost(System.Text.RegularExpressions.Match match, SourceLine line)
    {
        var playerName = match.Groups["name"].Value;
        if (!PmuValues.TryParseCents(match.Groups["amount"].Value, out var amount))
        {
            return Fail(ParseErrorCodes.InvalidNumber, line);
        }

        if (!seats.Exists(s => s.PlayerName == playerName))
        {
            return Fail(ParseErrorCodes.UnknownPlayer, line);
        }

        var type = match.Groups["kind"].Value == "SB"
            ? ParsedActionType.PostSmallBlind
            : ParsedActionType.PostBigBlind;

        // PMU encodes a dead combined blind (missed SB+BB) as a single "Post BB amount=SB+BB".
        // The live portion for raise calculations is the BB only; the dead SB is excluded.
        if (type == ParsedActionType.PostBigBlind && amount == smallBlind + bigBlind)
        {
            streetContributions[playerName] = bigBlind;
            currentBet = Math.Max(currentBet, bigBlind);
        }
        else
        {
            AddToStreet(playerName, amount);
        }

        DeductStack(playerName, amount);
        AddAction(playerName, type, amount, raiseTo: null, isAllIn: false);
        return true;
    }

    private bool ReadDealtOrFirstAction(SourceLine line)
    {
        var dealt = PmuPatterns.DealtTo().Match(line.Text);
        if (dealt.Success)
        {
            heroName = dealt.Groups["name"].Value;
            if (!PmuValues.TryParseCards(dealt.Groups["cards"].Value, out var cards) || cards.Count != 2)
            {
                return Fail(ParseErrorCodes.InvalidHoleCards, line);
            }

            heroCards = cards;
            section = Section.Actions;
            return true;
        }

        // Some hands have no "Dealt to" (hero not seated). Transition to Actions.
        section = Section.Actions;
        return ReadAction(line);
    }

    private bool ReadStreet(System.Text.RegularExpressions.Match match, SourceLine line)
    {
        var streetName = match.Groups["street"].Value;
        if (!PmuValues.TryParseCards(match.Groups["cards"].Value, out var cards))
        {
            return Fail(ParseErrorCodes.InvalidCard, line);
        }

        var (nextStreet, expectedNew) = streetName switch
        {
            "FLOP" => (Street.Flop, 3),
            "TURN" => (Street.Turn, 1),
            _ => (Street.River, 1),
        };

        if (cards.Count != expectedNew)
        {
            return Fail(ParseErrorCodes.InvalidBoard, line);
        }

        board.AddRange(cards);
        street = nextStreet;
        currentBet = 0;
        foreach (var name in streetContributions.Keys)
        {
            streetContributions[name] = 0;
        }

        return true;
    }

    private bool ReadAction(SourceLine line)
    {
        var text = line.Text;

        if (PmuPatterns.Fold().Match(text) is { Success: true } fold)
        {
            var name = fold.Groups["name"].Value;
            if (!KnownPlayer(name, line)) return false;
            AddAction(name, ParsedActionType.Fold, 0, raiseTo: null, isAllIn: false);
            return true;
        }

        if (PmuPatterns.Check().Match(text) is { Success: true } check)
        {
            var name = check.Groups["name"].Value;
            if (!KnownPlayer(name, line)) return false;
            AddAction(name, ParsedActionType.Check, 0, raiseTo: null, isAllIn: false);
            return true;
        }

        if (PmuPatterns.Call().Match(text) is { Success: true } call)
        {
            var name = call.Groups["name"].Value;
            if (!KnownPlayer(name, line)) return false;
            if (!PmuValues.TryParseCents(call.Groups["amount"].Value, out var amount))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            var callAllIn = DetectAllIn(name, amount);
            AddToStreet(name, amount);
            DeductStack(name, amount);
            AddAction(name, ParsedActionType.Call, amount, raiseTo: null, callAllIn);
            return true;
        }

        if (PmuPatterns.Bet().Match(text) is { Success: true } bet)
        {
            var name = bet.Groups["name"].Value;
            if (!KnownPlayer(name, line)) return false;
            if (!PmuValues.TryParseCents(bet.Groups["amount"].Value, out var amount))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            var betAllIn = DetectAllIn(name, amount);
            AddToStreet(name, amount);
            DeductStack(name, amount);
            AddAction(name, ParsedActionType.Bet, amount, raiseTo: null, betAllIn);
            return true;
        }

        if (PmuPatterns.Raise().Match(text) is { Success: true } raise)
        {
            return ReadRaise(raise, line, isAllIn: false);
        }

        if (PmuPatterns.Allin().Match(text) is { Success: true } allin)
        {
            return ReadAllin(allin, line);
        }

        return Fail(ParseErrorCodes.UnrecognizedLine, line);
    }

    private bool ReadRaise(System.Text.RegularExpressions.Match match, SourceLine line, bool isAllIn)
    {
        var name = match.Groups["name"].Value;
        if (!KnownPlayer(name, line)) return false;
        if (!PmuValues.TryParseCents(match.Groups["amount"].Value, out var raiseTo))
        {
            return Fail(ParseErrorCodes.InvalidNumber, line);
        }

        var alreadyIn = streetContributions[name];
        var increment = raiseTo - alreadyIn;
        if (increment <= 0)
        {
            return Fail(ParseErrorCodes.InconsistentRaise, line);
        }

        var raiseAllIn = isAllIn || DetectAllIn(name, increment);
        DeductStack(name, increment);
        AddAction(name, ParsedActionType.Raise, increment, raiseTo, raiseAllIn);
        streetContributions[name] = raiseTo;
        currentBet = raiseTo;
        return true;
    }

    private bool ReadAllin(System.Text.RegularExpressions.Match match, SourceLine line)
    {
        var name = match.Groups["name"].Value;
        if (!KnownPlayer(name, line)) return false;
        if (!PmuValues.TryParseCents(match.Groups["amount"].Value, out var amount))
        {
            return Fail(ParseErrorCodes.InvalidNumber, line);
        }

        var alreadyIn = streetContributions[name];
        var playerTotal = alreadyIn + amount;

        if (currentBet == 0)
        {
            AddToStreet(name, amount);
            AddAction(name, ParsedActionType.Bet, amount, raiseTo: null, isAllIn: true);
        }
        else if (playerTotal <= currentBet)
        {
            AddToStreet(name, amount);
            AddAction(name, ParsedActionType.Call, amount, raiseTo: null, isAllIn: true);
        }
        else
        {
            AddAction(name, ParsedActionType.Raise, amount, playerTotal, isAllIn: true);
            streetContributions[name] = playerTotal;
            currentBet = playerTotal;
        }

        DeductStack(name, amount);
        return true;
    }

    private bool ReadSummaryLine(SourceLine line)
    {
        var pot = PmuPatterns.TotalPot().Match(line.Text);
        if (pot.Success)
        {
            if (!PmuValues.TryParseCents(pot.Groups["pot"].Value, out var netPot)
                || !PmuValues.TryParseCents(pot.Groups["rake"].Value, out rake))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            totalPot = netPot + rake;
            return true;
        }

        if (PmuPatterns.Mucks().IsMatch(line.Text))
        {
            return true;
        }

        var shows = PmuPatterns.Shows().Match(line.Text);
        if (shows.Success)
        {
            if (!PmuValues.TryParseCards(shows.Groups["cards"].Value, out var cards) || cards.Count != 2)
            {
                return Fail(ParseErrorCodes.InvalidHoleCards, line);
            }

            shownCards.Add(new ParsedShownCards(shows.Groups["name"].Value, cards, shows.Groups["desc"].Value));
            return true;
        }

        var wins = PmuPatterns.Wins().Match(line.Text);
        if (wins.Success)
        {
            if (!PmuValues.TryParseCents(wins.Groups["amount"].Value, out var amount))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            var kind = collections.Count > 0 || rake > 0 ? PotKind.Main : PotKind.Single;
            collections.Add(new ParsedPotCollection(wins.Groups["name"].Value, amount, kind, null));
            return true;
        }

        return Fail(ParseErrorCodes.UnrecognizedLine, line);
    }

    private bool Validate(SourceLine lastLine)
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

        if (invested < totalPot || collectedTotal + rake != totalPot)
        {
            return Fail(ParseErrorCodes.PotMismatch, lastLine);
        }

        if (buttonSeat == 0 && seats.Count > 0)
        {
            // No DEALER tag found: fall back to SB poster.
            foreach (var action in actions)
            {
                if (action.Type == ParsedActionType.PostSmallBlind)
                {
                    var sbSeat = seats.Find(s => s.PlayerName == action.PlayerName);
                    if (sbSeat is not null)
                    {
                        buttonSeat = sbSeat.SeatNumber;
                        break;
                    }
                }
            }

            if (buttonSeat == 0)
            {
                buttonSeat = seats[0].SeatNumber;
            }
        }

        return true;
    }

    private bool KnownPlayer(string name, SourceLine line)
    {
        if (seats.Exists(s => s.PlayerName == name))
        {
            return true;
        }

        return Fail(ParseErrorCodes.UnknownPlayer, line);
    }

    private void AddToStreet(string player, long amount)
    {
        var total = streetContributions[player] + amount;
        streetContributions[player] = total;
        currentBet = Math.Max(currentBet, total);
    }

    private bool DetectAllIn(string playerName, long amount) =>
        remainingStacks.TryGetValue(playerName, out var remaining) && remaining > 0 && amount == remaining;

    private void DeductStack(string playerName, long amount)
    {
        if (remainingStacks.TryGetValue(playerName, out var remaining))
            remainingStacks[playerName] = Math.Max(0, remaining - amount);
    }

    /// <summary>
    /// PMU does not include uncalled bet returns in its collected lines. This adds the excess
    /// to the collections and adjusts the total pot so that the ParsedHand contract holds:
    /// TotalPot = Actions.Sum() = Collections.Sum() + Rake.
    /// </summary>
    private void NormalizeUncalledBetReturn()
    {
        long invested = 0;
        foreach (var action in actions)
            invested += action.Amount;

        var excess = invested - totalPot;
        if (excess <= 0)
            return;

        string? returnTo = null;
        long max = 0;
        var perPlayer = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var action in actions)
        {
            perPlayer.TryGetValue(action.PlayerName, out var current);
            perPlayer[action.PlayerName] = current + action.Amount;
        }

        foreach (var (player, total) in perPlayer)
        {
            if (total > max)
            {
                max = total;
                returnTo = player;
            }
        }

        if (returnTo is not null)
        {
            collections.Add(new ParsedPotCollection(returnTo, excess, PotKind.Single, null));
            totalPot = invested;
        }
    }

    private void AddAction(string player, ParsedActionType type, long amount, long? raiseTo, bool isAllIn) =>
        actions.Add(new ParsedAction(actions.Count + 1, street, player, type, amount, raiseTo, isAllIn));

    private bool Fail(string code, SourceLine line)
    {
        error ??= new ParseError(code, line.Number, handId.Length > 0 ? handId : null);
        return false;
    }
}
