using System.Text.RegularExpressions;
using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories.Winamax;

/// <summary>
/// Reads one Winamax tournament hand (header line to last summary line). Single use: create one per hand.
/// Amounts are integer tournament chips; parsed by <see cref="WinamaxValues.TryParseChips"/>.
/// </summary>
internal sealed class WinamaxHandReader(IReadOnlyList<SourceLine> block) : WinamaxHandReaderBase(block)
{
    protected override Regex PostPattern => WinamaxPatterns.Post();
    protected override Regex CallOrBetPattern => WinamaxPatterns.CallOrBet();
    protected override Regex RaisePattern => WinamaxPatterns.Raise();
    protected override Regex CollectedPattern => WinamaxPatterns.Collected();

    private HeaderInfo? header;
    private TableInfo? table;
    private long? totalPot;

    protected override bool TryParseAmount(string text, out long value) =>
        WinamaxValues.TryParseChips(text, out value);

    protected override bool IsReadComplete =>
        header is not null && table is not null && totalPot is not null;

    protected override bool ReadHeader(SourceLine line)
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

    protected override bool ReadTable(SourceLine line)
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

    protected override bool ReadSeat(SourceLine line)
    {
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

    protected override LineResult ReadPotLine(SourceLine line)
    {
        var total = WinamaxPatterns.TotalPot().Match(line.Text);
        if (!total.Success)
        {
            return LineResult.NotMatched;
        }

        if (totalPot is not null)
        {
            Fail(ParseErrorCodes.UnrecognizedLine, line);
            return LineResult.Rejected;
        }

        if (!WinamaxValues.TryParseChips(total.Groups["amount"].Value, out var amount))
        {
            Fail(ParseErrorCodes.InvalidNumber, line);
            return LineResult.Rejected;
        }

        totalPot = amount;
        return LineResult.Accepted;
    }

    protected override ParsedHand? ValidateAndBuild(SourceLine lastLine)
    {
        var pot = totalPot!.Value;

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
            Fail(ParseErrorCodes.PotMismatch, lastLine);
            return null;
        }

        var boardMatches = summaryBoard is null ? board.Count == 0 : summaryBoard.SequenceEqual(board);
        if (!boardMatches)
        {
            Fail(ParseErrorCodes.BoardMismatch, lastLine);
            return null;
        }

        return new ParsedHand
        {
            Room = PokerRoom.Winamax,
            GameType = table!.TournamentId is null ? GameType.CashGame : GameType.Tournament,
            ExternalHandId = header!.HandId,
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
            LineNumber = FirstLineNumber,
        };
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
