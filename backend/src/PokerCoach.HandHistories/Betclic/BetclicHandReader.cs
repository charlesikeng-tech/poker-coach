using System.Globalization;
using System.Text.RegularExpressions;
using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories.Betclic;

internal sealed class BetclicHandReader(IReadOnlyList<SourceLine> block)
{
    internal const int MaxLineLength = 1_000;

    private enum Section
    {
        Header,
        Players,
        HoleCards,
        Actions,
        Showdown,
        Summary,
    }

    private readonly List<ParsedSeat> seats = [];
    private readonly List<ParsedAction> actions = [];
    private readonly List<Card> board = [];
    private readonly List<ParsedShownCards> shownCards = [];
    private readonly List<ParsedPotCollection> collections = [];
    private readonly Dictionary<string, long> streetContributions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Card>> holeCards = new(StringComparer.Ordinal);

    private Section section = Section.Header;
    private Street street = Street.Preflop;
    private long currentBet;
    private string? handId;
    private string? heroName;
    private string? mandatorySbPlayer;
    private string? mandatoryBbPlayer;
    private List<Card> heroCards = [];
    private HeaderInfo? header;
    private long? totalPot;
    private long rake;
    private ParseError? error;

    // Tournament state.
    private bool isTournament;
    private string? externalTournamentId;
    private string? tournamentName;
    private readonly List<ParsedElimination> eliminations = [];
    private readonly List<ParsedBountyAward> bountyAwards = [];
    private HandBuyIn? buyIn;

    public (ParsedHand? Hand, ParseError? Error) Read()
    {
        for (var i = 0; i < block.Count; i++)
        {
            if (!ReadLine(block[i]))
            {
                return (null, error);
            }
        }

        var lastLine = block[^1];
        if (section != Section.Summary || header is null || totalPot is not { } pot)
        {
            Fail(ParseErrorCodes.IncompleteHand, lastLine);
            return (null, error);
        }

        if (seats.Count == 0)
        {
            Fail(ParseErrorCodes.MissingSeats, lastLine);
            return (null, error);
        }

        if (!Validate(pot, lastLine))
        {
            return (null, error);
        }

        UpgradeSingleToMainWhenSidePotsExist();
        pot = NormalizeUncalledBetReturn(pot);

        var hand = new ParsedHand
        {
            Room = PokerRoom.Betclic,
            GameType = isTournament ? GameType.Tournament : GameType.CashGame,
            ExternalHandId = header.HandId,
            TournamentName = tournamentName,
            ExternalTournamentId = externalTournamentId,
            BuyIn = buyIn,
            TableName = header.TableId,
            MaxSeats = isTournament ? seats.Max(s => s.SeatNumber) : header.MaxSeats,
            IsRealMoney = true,
            Level = 0,
            Ante = null,
            SmallBlind = header.SmallBlind,
            BigBlind = header.BigBlind,
            StartedAt = header.StartedAt,
            ButtonSeat = FindButtonSeat(),
            Seats = seats,
            HeroName = heroName,
            HeroCards = heroCards,
            Actions = actions,
            Board = board,
            ShownCards = shownCards,
            Collections = collections,
            TotalPot = pot,
            Rake = isTournament ? null : rake,
            Stakes = isTournament ? null : header.Stakes,
            Eliminations = eliminations,
            BountyAwards = bountyAwards,
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

        if (text == BetclicValues.HeaderMarker)
        {
            return section == Section.Header;
        }

        if (text == BetclicValues.PlayersMarker)
        {
            return section == Section.Header && header is not null ? Enter(Section.Players) : Fail(ParseErrorCodes.UnexpectedSection, line);
        }

        if (text == BetclicValues.HoleCardsMarker)
        {
            if (section != Section.Players || seats.Count == 0)
            {
                return Fail(ParseErrorCodes.UnexpectedSection, line);
            }

            return Enter(Section.HoleCards);
        }

        if (text == BetclicValues.PreflopMarker)
        {
            return section == Section.HoleCards ? Enter(Section.Actions) : Fail(ParseErrorCodes.UnexpectedSection, line);
        }

        if (text == BetclicValues.ShowdownMarker)
        {
            return section == Section.Actions ? Enter(Section.Showdown) : Fail(ParseErrorCodes.UnexpectedSection, line);
        }

        if (text == BetclicValues.SummaryMarker)
        {
            return section is Section.Actions or Section.Showdown
                ? Enter(Section.Summary)
                : Fail(ParseErrorCodes.UnexpectedSection, line);
        }

        var streetMarker = BetclicPatterns.StreetMarker().Match(text);
        if (streetMarker.Success)
        {
            return section == Section.Actions ? ReadStreet(streetMarker, line) : Fail(ParseErrorCodes.UnexpectedSection, line);
        }

        return section switch
        {
            Section.Header => ReadHeaderField(line),
            Section.Players => ReadSeat(line),
            Section.HoleCards => ReadHoleCards(line),
            Section.Actions or Section.Showdown => ReadActionOrShow(line),
            Section.Summary => ReadSummaryLine(line),
            _ => Fail(ParseErrorCodes.UnrecognizedLine, line),
        };
    }

    private bool ReadHeaderField(SourceLine line)
    {
        var text = line.Text;

        if (text.StartsWith("Hand ID: ", StringComparison.Ordinal))
        {
            handId = text["Hand ID: ".Length..];
            if (header is not null)
            {
                header = header with { HandId = handId };
            }

            return true;
        }

        if (text.StartsWith("Site: ", StringComparison.Ordinal)
            || text.StartsWith("Game Type: ", StringComparison.Ordinal))
        {
            return true;
        }

        if (text.StartsWith("Game Mode: ", StringComparison.Ordinal))
        {
            isTournament = text["Game Mode: ".Length..] == "Tournament";
            return true;
        }

        if (text.StartsWith("Tournament Type: ", StringComparison.Ordinal))
        {
            return true;
        }

        if (text.StartsWith("Game ID: ", StringComparison.Ordinal))
        {
            if (isTournament)
            {
                externalTournamentId = text["Game ID: ".Length..];
            }

            return true;
        }

        if (text.StartsWith("Buy In: ", StringComparison.Ordinal))
        {
            if (!BetclicValues.TryParseBuyIn(text["Buy In: ".Length..], out var amount))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            buyIn = new HandBuyIn(amount, 0m, BetclicValues.Currency);
            return true;
        }

        if (text.StartsWith("Game Name: ", StringComparison.Ordinal))
        {
            return ReadGameName(text["Game Name: ".Length..], line);
        }

        if (text.StartsWith("Table ID: ", StringComparison.Ordinal))
        {
            if (header is not null)
            {
                header = header with { TableId = text["Table ID: ".Length..] };
            }

            return true;
        }

        if (text.StartsWith("Date & Time: ", StringComparison.Ordinal))
        {
            var dateText = text["Date & Time: ".Length..];
            if (dateText.EndsWith(" (UTC)", StringComparison.Ordinal))
            {
                dateText = dateText[..^" (UTC)".Length];
            }

            if (!BetclicValues.TryParseUtc(dateText, out var startedAt))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            if (header is not null)
            {
                header = header with { StartedAt = startedAt };
            }

            return true;
        }

        if (text.StartsWith("Blinds: ", StringComparison.Ordinal))
        {
            return ReadBlinds(text["Blinds: ".Length..], line);
        }

        if (text.StartsWith("Total Pot: ", StringComparison.Ordinal))
        {
            var potText = text["Total Pot: ".Length..];
            if (!TryParsePotAmount(potText, out var pot))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            totalPot = pot;
            return true;
        }

        if (text.StartsWith("Rake: ", StringComparison.Ordinal))
        {
            var rakeText = text["Rake: ".Length..];
            if (!TryParsePotAmount(rakeText, out var rakeValue))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            rake = rakeValue;
            return true;
        }

        return Fail(ParseErrorCodes.UnrecognizedLine, line);
    }

    private bool ReadGameName(string gameName, SourceLine line)
    {
        if (isTournament)
        {
            tournamentName = gameName;
            header = new HeaderInfo(handId ?? string.Empty, string.Empty, DateTimeOffset.MinValue, null, 0, 0, 0);
            return true;
        }

        // "NLHE 0.25/0.5 6 max Deep" or "NLHE 0.10/0.25 6 Max Deep"
        var parts = gameName.Split(' ');
        if (parts.Length < 3)
        {
            return Fail(ParseErrorCodes.MissingField, line);
        }

        var stakes = parts[1];
        if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var maxSeats))
        {
            return Fail(ParseErrorCodes.InvalidNumber, line);
        }

        header = new HeaderInfo(handId ?? string.Empty, string.Empty, DateTimeOffset.MinValue, stakes, 0, 0, maxSeats);
        return true;
    }

    private bool ReadBlinds(string blindsText, SourceLine line)
    {
        var slash = blindsText.IndexOf('/');
        if (slash < 0)
        {
            return Fail(ParseErrorCodes.MissingField, line);
        }

        long sb, bb;
        if (isTournament)
        {
            if (!BetclicValues.TryParseChips(blindsText[..slash], out sb)
                || !BetclicValues.TryParseChips(blindsText[(slash + 1)..], out bb))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }
        }
        else
        {
            if (!BetclicValues.TryParseEuroAmount(blindsText[..slash], out sb)
                || !BetclicValues.TryParseEuroAmount(blindsText[(slash + 1)..], out bb))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }
        }

        if (header is not null)
        {
            header = header with { SmallBlind = sb, BigBlind = bb };
        }

        return true;
    }

    private bool ReadSeat(SourceLine line)
    {
        var match = isTournament
            ? BetclicPatterns.TournamentSeatLine().Match(line.Text)
            : BetclicPatterns.SeatLine().Match(line.Text);

        if (!match.Success)
        {
            return Fail(ParseErrorCodes.UnrecognizedLine, line);
        }

        if (!int.TryParse(match.Groups["seat"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seatNumber))
        {
            return Fail(ParseErrorCodes.InvalidNumber, line);
        }

        long stack;
        if (isTournament)
        {
            if (!BetclicValues.TryParseChips(match.Groups["stack"].Value, out stack))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }
        }
        else
        {
            if (!BetclicValues.TryParseCents(match.Groups["stack"].Value, out stack))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }
        }

        var name = match.Groups["name"].Value;
        if (streetContributions.ContainsKey(name) || seats.Exists(s => s.SeatNumber == seatNumber))
        {
            return Fail(ParseErrorCodes.DuplicatePlayer, line);
        }

        var tags = match.Groups["tags"].Value;
        if (tags.Contains("Hero", StringComparison.Ordinal))
            heroName = name;
        if (tags.Contains("SB", StringComparison.Ordinal))
            mandatorySbPlayer = name;
        if (tags.Contains("BB", StringComparison.Ordinal))
            mandatoryBbPlayer = name;

        decimal? bounty = null;
        if (isTournament && match.Groups["bounty"].Success
            && decimal.TryParse(match.Groups["bounty"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var bountyValue))
        {
            bounty = bountyValue;
        }

        seats.Add(new ParsedSeat(seatNumber, name, stack, bounty));
        streetContributions[name] = 0;
        return true;
    }

    private bool ReadHoleCards(SourceLine line)
    {
        // "Héros: [6c Jh]" or "Vilain 6: [4d 4s]"
        var text = line.Text;
        var colonBracket = text.IndexOf(": [", StringComparison.Ordinal);
        if (colonBracket < 0 || text[^1] != ']')
        {
            return Fail(ParseErrorCodes.UnrecognizedLine, line);
        }

        var playerName = text[..colonBracket];
        var cardsText = text[(colonBracket + 3)..^1];

        if (!BetclicValues.TryParseCards(cardsText, out var cards) || cards.Count != 2)
        {
            return Fail(ParseErrorCodes.InvalidHoleCards, line);
        }

        if (!seats.Exists(s => s.PlayerName == playerName))
        {
            return Fail(ParseErrorCodes.UnknownPlayer, line);
        }

        holeCards[playerName] = cards;
        if (playerName == heroName)
        {
            heroCards = cards;
        }

        return true;
    }

    private bool ReadStreet(Match match, SourceLine line)
    {
        var streetName = match.Groups["street"].Value;
        var allCardsText = match.Groups["cards"].Value;

        if (!BetclicValues.TryParseCards(allCardsText, out var allCards))
        {
            return Fail(ParseErrorCodes.InvalidCard, line);
        }

        var (nextStreet, expectedTotal) = streetName switch
        {
            "FLOP" => (Street.Flop, 3),
            "TURN" => (Street.Turn, 4),
            _ => (Street.River, 5),
        };

        if (allCards.Count != expectedTotal)
        {
            return Fail(ParseErrorCodes.InvalidBoard, line);
        }

        // Betclic prints all board cards on each street line; extract only the new ones.
        var newCards = allCards.Skip(board.Count).ToList();
        var expectedNew = expectedTotal - board.Count;
        if (newCards.Count != expectedNew)
        {
            return Fail(ParseErrorCodes.InvalidBoard, line);
        }

        // Verify previously dealt cards match.
        for (var i = 0; i < board.Count; i++)
        {
            if (board[i] != allCards[i])
            {
                return Fail(ParseErrorCodes.BoardMismatch, line);
            }
        }

        board.AddRange(newCards);
        street = nextStreet;
        currentBet = 0;
        foreach (var name in streetContributions.Keys)
        {
            streetContributions[name] = 0;
        }

        return true;
    }

    private bool ReadActionOrShow(SourceLine line)
    {
        var actionMatch = BetclicPatterns.ActionLine().Match(line.Text);
        if (!actionMatch.Success)
        {
            // Showdown shows line (no timestamp prefix).
            if (section == Section.Showdown)
            {
                return ReadShowdownLine(line);
            }

            return Fail(ParseErrorCodes.UnrecognizedLine, line);
        }

        var playerName = actionMatch.Groups["name"].Value;
        var actionText = actionMatch.Groups["action"].Value;

        // Noise lines.
        if (actionText is "Sits in" or "Sits out" or "Disconnected" or "Reconnected")
        {
            return true;
        }

        if (!seats.Exists(s => s.PlayerName == playerName))
        {
            return Fail(ParseErrorCodes.UnknownPlayer, line);
        }

        var postMatch = isTournament
            ? BetclicPatterns.TournamentPost().Match(actionText)
            : BetclicPatterns.Post().Match(actionText);

        if (postMatch.Success)
        {
            return ReadPost(playerName, postMatch, line);
        }

        if (BetclicPatterns.Fold().IsMatch(actionText))
        {
            AddAction(playerName, ParsedActionType.Fold, 0, raiseTo: null, isAllIn: false);
            return true;
        }

        if (BetclicPatterns.Check().IsMatch(actionText))
        {
            AddAction(playerName, ParsedActionType.Check, 0, raiseTo: null, isAllIn: false);
            return true;
        }

        var callMatch = isTournament
            ? BetclicPatterns.TournamentCall().Match(actionText)
            : BetclicPatterns.Call().Match(actionText);

        if (callMatch.Success)
        {
            if (!TryParseActionAmount(callMatch.Groups["amount"].Value, out var amount))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            AddToStreet(playerName, amount);
            AddAction(playerName, ParsedActionType.Call, amount, raiseTo: null, callMatch.Groups["allIn"].Success);
            return true;
        }

        var betMatch = isTournament
            ? BetclicPatterns.TournamentBet().Match(actionText)
            : BetclicPatterns.Bet().Match(actionText);

        if (betMatch.Success)
        {
            if (!TryParseActionAmount(betMatch.Groups["amount"].Value, out var amount))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            AddToStreet(playerName, amount);
            AddAction(playerName, ParsedActionType.Bet, amount, raiseTo: null, betMatch.Groups["allIn"].Success);
            return true;
        }

        var raiseMatch = isTournament
            ? BetclicPatterns.TournamentRaise().Match(actionText)
            : BetclicPatterns.Raise().Match(actionText);

        if (raiseMatch.Success)
        {
            if (!TryParseActionAmount(raiseMatch.Groups["to"].Value, out var raiseTo))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            var alreadyIn = streetContributions[playerName];
            var increment = raiseTo - alreadyIn;
            if (increment <= 0 || raiseTo <= currentBet)
            {
                return Fail(ParseErrorCodes.InconsistentRaise, line);
            }

            AddAction(playerName, ParsedActionType.Raise, increment, raiseTo, raiseMatch.Groups["allIn"].Success);
            streetContributions[playerName] = raiseTo;
            currentBet = raiseTo;
            return true;
        }

        return Fail(ParseErrorCodes.UnrecognizedLine, line);
    }

    private bool ReadPost(string playerName, Match post, SourceLine line)
    {
        if (!TryParseActionAmount(post.Groups["amount"].Value, out var amount))
        {
            return Fail(ParseErrorCodes.InvalidNumber, line);
        }

        var type = post.Groups["kind"].Value == "SB"
            ? ParsedActionType.PostSmallBlind
            : ParsedActionType.PostBigBlind;

        // Dead blind detection: a player posting outside their mandatory position (seat tag)
        // posts a dead blind. Dead blinds go to the pot (kept in actions) but the player's
        // raise base is only their live portion — not the dead SB they already posted.
        // Fallback to normal when seat tags are absent (mandatoryXxPlayer is null).
        var isMandatory = type == ParsedActionType.PostSmallBlind
            ? (mandatorySbPlayer is null || playerName == mandatorySbPlayer)
            : (mandatoryBbPlayer is null || playerName == mandatoryBbPlayer);

        if (isMandatory)
        {
            AddToStreet(playerName, amount);
        }
        else if (type == ParsedActionType.PostBigBlind)
        {
            // Non-mandatory BB (sitting-in player): BB is their live bet regardless of any
            // dead SB they may have posted before. Reset contribution to BB only.
            streetContributions[playerName] = amount;
            currentBet = Math.Max(currentBet, amount);
        }
        // Non-mandatory SB: dead blind — no contribution to streetContributions.

        var isAllIn = post.Groups["allIn"].Success;
        AddAction(playerName, type, amount, raiseTo: null, isAllIn: isAllIn);
        return true;
    }

    private bool ReadShowdownLine(SourceLine line)
    {
        var shows = BetclicPatterns.Shows().Match(line.Text);
        if (shows.Success)
        {
            var playerName = shows.Groups["name"].Value;
            if (!BetclicValues.TryParseCards(shows.Groups["cards"].Value, out var cards) || cards.Count != 2)
            {
                return Fail(ParseErrorCodes.InvalidHoleCards, line);
            }

            shownCards.Add(new ParsedShownCards(playerName, cards, shows.Groups["description"].Value));
            return true;
        }

        return Fail(ParseErrorCodes.UnrecognizedLine, line);
    }

    private bool ReadSummaryLine(SourceLine line)
    {
        // Tournament elimination and bounty lines.
        if (isTournament)
        {
            var finished = BetclicPatterns.FinishedLine().Match(line.Text);
            if (finished.Success)
            {
                if (int.TryParse(finished.Groups["position"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var position))
                {
                    eliminations.Add(new ParsedElimination(finished.Groups["name"].Value, position));
                }

                return true;
            }

            var bounty = BetclicPatterns.BountyLine().Match(line.Text);
            if (bounty.Success)
            {
                if (decimal.TryParse(bounty.Groups["amount"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var bountyAmount))
                {
                    bountyAwards.Add(new ParsedBountyAward(
                        bounty.Groups["winner"].Value,
                        bounty.Groups["eliminated"].Value,
                        bountyAmount,
                        BetclicValues.Currency));
                }

                return true;
            }
        }

        var wins = isTournament
            ? BetclicPatterns.TournamentWins().Match(line.Text)
            : BetclicPatterns.Wins().Match(line.Text);

        if (!wins.Success)
        {
            return Fail(ParseErrorCodes.UnrecognizedLine, line);
        }

        var playerName = wins.Groups["name"].Value;
        if (!TryParseActionAmount(wins.Groups["amount"].Value, out var amount))
        {
            return Fail(ParseErrorCodes.InvalidNumber, line);
        }

        var sideOrd = wins.Groups["sideOrd"];
        if (sideOrd.Success)
        {
            if (!int.TryParse(sideOrd.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var sideNumber))
            {
                return Fail(ParseErrorCodes.InvalidNumber, line);
            }

            collections.Add(new ParsedPotCollection(playerName, amount, PotKind.Side, sideNumber));
        }
        else
        {
            var kind = collections.Count > 0 || rake > 0 ? PotKind.Main : PotKind.Single;
            collections.Add(new ParsedPotCollection(playerName, amount, kind, null));
        }

        return true;
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

        // collected + rake must equal the declared pot.
        // invested >= pot: uncalled bets (returned to winner) make invested > pot.
        if (invested < pot || collectedTotal + rake != pot)
        {
            return Fail(ParseErrorCodes.PotMismatch, lastLine);
        }

        return true;
    }

    private int FindButtonSeat()
    {
        // Find the seat tagged [BTN].
        foreach (var seat in seats)
        {
            // The tag was parsed from the seat line; we stored it as part of the name extraction.
            // We need to check the original block for the BTN tag.
        }

        // Fall back: scan the block for BTN tag.
        foreach (var line in block)
        {
            if (line.Text.StartsWith("Seat ", StringComparison.Ordinal) && line.Text.Contains("[BTN", StringComparison.Ordinal))
            {
                var match = isTournament
                    ? BetclicPatterns.TournamentSeatLine().Match(line.Text)
                    : BetclicPatterns.SeatLine().Match(line.Text);

                if (match.Success && int.TryParse(match.Groups["seat"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seatNumber))
                {
                    return seatNumber;
                }
            }
        }

        // Heads-up or no BTN tag: SB is the button. Find first SB poster.
        foreach (var action in actions)
        {
            if (action.Type == ParsedActionType.PostSmallBlind)
            {
                var sbSeat = seats.Find(s => s.PlayerName == action.PlayerName);
                if (sbSeat is not null)
                {
                    return sbSeat.SeatNumber;
                }
            }
        }

        return seats.Count > 0 ? seats[0].SeatNumber : 1;
    }

    /// <summary>
    /// When side pots exist, the "wins main pot" collection was initially marked Single
    /// (before the side pot was seen). Upgrade it to Main. Runs before NormalizeUncalledBetReturn
    /// so that uncalled returns added later keep their Single kind.
    /// </summary>
    private void UpgradeSingleToMainWhenSidePotsExist()
    {
        if (!collections.Exists(c => c.Kind == PotKind.Side))
        {
            return;
        }

        for (var i = 0; i < collections.Count; i++)
        {
            if (collections[i].Kind == PotKind.Single)
            {
                collections[i] = collections[i] with { Kind = PotKind.Main };
            }
        }
    }

    private bool TryParsePotAmount(string text, out long amount) =>
        isTournament
            ? BetclicValues.TryParseChips(text, out amount)
            : BetclicValues.TryParseEuroAmount(text, out amount);

    private bool TryParseActionAmount(string text, out long amount) =>
        isTournament
            ? BetclicValues.TryParseChips(text, out amount)
            : BetclicValues.TryParseCents(text, out amount);

    private void AddToStreet(string player, long amount)
    {
        var total = streetContributions[player] + amount;
        streetContributions[player] = total;
        currentBet = Math.Max(currentBet, total);
    }

    /// <summary>
    /// Betclic does not include uncalled bet returns in its collected lines. This adds the excess
    /// to the collections and adjusts the total pot so that the ParsedHand contract holds:
    /// TotalPot = Actions.Sum() = Collections.Sum() + Rake.
    /// </summary>
    private long NormalizeUncalledBetReturn(long pot)
    {
        long invested = 0;
        foreach (var action in actions)
            invested += action.Amount;

        var excess = invested - pot;
        if (excess <= 0)
            return pot;

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
            return invested;
        }

        return pot;
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

    private sealed record HeaderInfo(
        string HandId,
        string TableId,
        DateTimeOffset StartedAt,
        string? Stakes,
        long SmallBlind,
        long BigBlind,
        int MaxSeats);
}
