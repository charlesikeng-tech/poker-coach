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
internal sealed class WinamaxCashGameHandReader(IReadOnlyList<SourceLine> block) : WinamaxHandReaderBase(block)
{
    protected override Regex PostPattern => WinamaxPatterns.CashPost();
    protected override Regex CallOrBetPattern => WinamaxPatterns.CashCallOrBet();
    protected override Regex RaisePattern => WinamaxPatterns.CashRaise();
    protected override Regex CollectedPattern => WinamaxPatterns.CashCollected();

    private CashHeaderInfo? header;
    private string? tableName;
    private int maxSeats;
    private int buttonSeat;
    private long? netPot;
    private long rake;

    protected override bool TryParseAmount(string text, out long value)
    {
        if (!WinamaxValues.TryParseEuros(text, out var euros))
        {
            value = 0;
            return false;
        }

        value = EurosToCents(euros);
        return true;
    }

    protected override bool IsReadComplete =>
        header is not null && tableName is not null && netPot is not null;

    protected override bool ReadHeader(SourceLine line)
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

        var sbDisplay = sbEuros.ToString("0.##");
        var bbDisplay = bbEuros.ToString("0.##");

        header = new CashHeaderInfo(id, sb, bb, $"{sbDisplay}/{bbDisplay}", startedAt);
        return true;
    }

    protected override bool ReadTable(SourceLine line)
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

    protected override bool ReadSeat(SourceLine line)
    {
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

    protected override LineResult ReadPotLine(SourceLine line)
    {
        var total = WinamaxPatterns.CashTotalPot().Match(line.Text);
        if (!total.Success)
        {
            return LineResult.NotMatched;
        }

        if (netPot is not null)
        {
            Fail(ParseErrorCodes.UnrecognizedLine, line);
            return LineResult.Rejected;
        }

        if (!WinamaxValues.TryParseEuros(total.Groups["amount"].Value, out var netEuros))
        {
            Fail(ParseErrorCodes.InvalidNumber, line);
            return LineResult.Rejected;
        }

        netPot = EurosToCents(netEuros);

        var rakeGroup = total.Groups["rake"];
        if (rakeGroup.Success)
        {
            if (!WinamaxValues.TryParseEuros(rakeGroup.Value, out var rakeEuros))
            {
                Fail(ParseErrorCodes.InvalidNumber, line);
                return LineResult.Rejected;
            }

            rake = EurosToCents(rakeEuros);
        }

        return LineResult.Accepted;
    }

    protected override ParsedHand? ValidateAndBuild(SourceLine lastLine)
    {
        var net = netPot!.Value;
        var grossPot = net + rake;

        var invested = actions.Sum(a => a.Amount);
        var collectedTotal = collections.Sum(c => c.Amount);

        if (invested != grossPot || collectedTotal != net)
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
            GameType = GameType.CashGame,
            ExternalHandId = header!.HandId,
            TournamentName = null,
            ExternalTournamentId = null,
            BuyIn = null,
            TableName = tableName!,
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
            LineNumber = FirstLineNumber,
        };
    }

    private static long EurosToCents(decimal euros) => (long)Math.Round(euros * 100m, MidpointRounding.AwayFromZero);

    private sealed record CashHeaderInfo(string HandId, long SmallBlind, long BigBlind, string Stakes, DateTimeOffset StartedAt);
}
