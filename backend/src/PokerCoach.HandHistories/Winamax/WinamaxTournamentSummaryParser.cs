using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories.Winamax;

/// <summary>
/// Parses a Winamax tournament summary file. All or nothing: any unknown line or missing mandatory
/// field rejects the summary, because tournament results feed ROI and must never be half-read.
/// </summary>
public sealed class WinamaxTournamentSummaryParser : ITournamentSummaryParser
{
    // The "Levels" line holds the whole blind structure on one line (several kB).
    private const int MaxLineLength = 20_000;
    private const string LevelsPrefix = "Levels : ";

    // Sanity bound for untrusted input (keeps TimeSpan construction in range).
    private const int MaxPlayedHours = 1_000;

    public PokerRoom Room => PokerRoom.Winamax;

    public TournamentSummaryParseResult Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var lines = WinamaxValues.SplitLines(content);
        var fields = new SummaryFields();
        var headerRead = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var text = lines[index];
            var lineNumber = index + 1;
            if (text.Length == 0)
            {
                continue;
            }

            if (text.Length > MaxLineLength)
            {
                return Failure(ParseErrorCodes.LineTooLong, lineNumber);
            }

            if (!headerRead)
            {
                var header = WinamaxPatterns.SummaryHeader().Match(text);
                if (!header.Success)
                {
                    return Failure(ParseErrorCodes.UnrecognizedFormat, lineNumber);
                }

                fields.TournamentName = header.Groups["name"].Value;
                fields.TournamentId = header.Groups["id"].Value;
                fields.LateRegistration = header.Groups["late"].Success;
                headerRead = true;
                continue;
            }

            var code = ReadLine(text, fields);
            if (code is not null)
            {
                return Failure(code, lineNumber);
            }
        }

        if (!headerRead)
        {
            return Failure(ParseErrorCodes.EmptyFile, 1);
        }

        if (fields.TournamentId is null || fields.TournamentName is null || fields.PlayerName is null
            || fields.PrizePoolBuyIn is not { } prizePoolBuyIn || fields.Fee is not { } fee
            || fields.RegisteredPlayers is not { } registeredPlayers || fields.PrizePool is not { } prizePool
            || fields.StartedAt is not { } startedAt)
        {
            return Failure(ParseErrorCodes.MissingField, lines.Length);
        }

        var summary = new ParsedTournamentSummary
        {
            Room = PokerRoom.Winamax,
            ExternalTournamentId = fields.TournamentId,
            TournamentName = fields.TournamentName,
            PlayerName = fields.PlayerName,
            PrizePoolBuyIn = prizePoolBuyIn,
            BountyBuyIn = fields.BountyBuyIn,
            Fee = fee,
            Currency = WinamaxValues.Currency,
            RegisteredPlayers = registeredPlayers,
            Mode = fields.Mode,
            Type = fields.Type,
            Speed = fields.Speed,
            FlightId = fields.FlightId,
            PrizePool = prizePool,
            StartedAt = startedAt,
            PlayedDuration = fields.PlayedDuration,
            FinishPosition = fields.FinishPosition,
            PrizeWinnings = fields.PrizeWinnings,
            BountyWinnings = fields.BountyWinnings,
            LateRegistration = fields.LateRegistration,
        };
        return new TournamentSummaryParseResult(summary, []);
    }

    /// <returns>An error code, or null when the line was understood.</returns>
    private static string? ReadLine(string text, SummaryFields fields)
    {
        // The blind structure is not used yet; level and blinds are read from each hand.
        if (text.StartsWith(LevelsPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var field = WinamaxPatterns.SummaryField().Match(text);
        if (field.Success)
        {
            return ReadField(field.Groups["key"].Value, field.Groups["value"].Value, fields);
        }

        var started = WinamaxPatterns.SummaryStarted().Match(text);
        if (started.Success)
        {
            if (!WinamaxValues.TryParseUtc(started.Groups["date"].Value, out var startedAt))
            {
                return ParseErrorCodes.InvalidNumber;
            }

            fields.StartedAt = startedAt;
            return null;
        }

        var played = WinamaxPatterns.SummaryPlayed().Match(text);
        if (played.Success)
        {
            var hours = 0;
            var minutes = 0;
            if ((played.Groups["hours"].Success && !WinamaxValues.TryParseInt(played.Groups["hours"].Value, out hours))
                || (played.Groups["minutes"].Success && !WinamaxValues.TryParseInt(played.Groups["minutes"].Value, out minutes))
                || !WinamaxValues.TryParseInt(played.Groups["seconds"].Value, out var seconds)
                || hours > MaxPlayedHours || minutes >= 60 || seconds >= 60)
            {
                return ParseErrorCodes.InvalidNumber;
            }

            fields.PlayedDuration = new TimeSpan(hours, minutes, seconds);
            return null;
        }

        var finished = WinamaxPatterns.SummaryFinished().Match(text);
        if (finished.Success)
        {
            if (!WinamaxValues.TryParseInt(finished.Groups["position"].Value, out var position) || position == 0)
            {
                return ParseErrorCodes.InvalidNumber;
            }

            fields.FinishPosition = position;
            return null;
        }

        var won = WinamaxPatterns.SummaryWon().Match(text);
        if (won.Success)
        {
            if (!WinamaxValues.TryParseEuros(won.Groups["prize"].Value, out var prize))
            {
                return ParseErrorCodes.InvalidNumber;
            }

            fields.PrizeWinnings = prize;
            if (won.Groups["bounty"].Success)
            {
                if (!WinamaxValues.TryParseEuros(won.Groups["bounty"].Value, out var bounty))
                {
                    return ParseErrorCodes.InvalidNumber;
                }

                fields.BountyWinnings = bounty;
            }

            return null;
        }

        return ParseErrorCodes.UnrecognizedLine;
    }

    private static string? ReadField(string key, string value, SummaryFields fields)
    {
        switch (key)
        {
            case "Player":
                fields.PlayerName = value;
                return null;
            case "Buy-In":
                return ReadBuyIn(value, fields);
            case "Registered players":
                if (!WinamaxValues.TryParseInt(value, out var registered))
                {
                    return ParseErrorCodes.InvalidNumber;
                }

                fields.RegisteredPlayers = registered;
                return null;
            case "Prizepool":
                if (!WinamaxValues.TryParseEurosWithSign(value, out var prizePool))
                {
                    return ParseErrorCodes.InvalidNumber;
                }

                fields.PrizePool = prizePool;
                return null;
            case "Mode":
                fields.Mode = value;
                return null;
            case "Type":
                fields.Type = value;
                return null;
            case "Speed":
                fields.Speed = value;
                return null;
            case "Flight ID":
                fields.FlightId = value;
                return null;
            default:
                return ParseErrorCodes.UnrecognizedLine;
        }
    }

    /// <summary>
    /// "4€ + 5€ + 1€" = prize pool + bounty + fee (confirmed on two knockout samples: the prize pool is a
    /// whole multiple of the first part only). "4€ + 1€" = prize pool + fee — to confirm on a non-knockout sample.
    /// </summary>
    private static string? ReadBuyIn(string value, SummaryFields fields)
    {
        var parts = value.Split(" + ");
        var amounts = new decimal[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!WinamaxValues.TryParseEurosWithSign(parts[i], out amounts[i]))
            {
                return ParseErrorCodes.UnsupportedBuyIn;
            }
        }

        switch (amounts.Length)
        {
            case 3:
                fields.PrizePoolBuyIn = amounts[0];
                fields.BountyBuyIn = amounts[1];
                fields.Fee = amounts[2];
                return null;
            case 2:
                fields.PrizePoolBuyIn = amounts[0];
                fields.Fee = amounts[1];
                return null;
            default:
                return ParseErrorCodes.UnsupportedBuyIn;
        }
    }

    private static TournamentSummaryParseResult Failure(string code, int lineNumber) =>
        new(null, [new ParseError(code, lineNumber, null)]);

    private sealed class SummaryFields
    {
        public string? TournamentId { get; set; }

        public string? TournamentName { get; set; }

        public string? PlayerName { get; set; }

        public decimal? PrizePoolBuyIn { get; set; }

        public decimal? BountyBuyIn { get; set; }

        public decimal? Fee { get; set; }

        public int? RegisteredPlayers { get; set; }

        public string? Mode { get; set; }

        public string? Type { get; set; }

        public string? Speed { get; set; }

        public string? FlightId { get; set; }

        public decimal? PrizePool { get; set; }

        public DateTimeOffset? StartedAt { get; set; }

        public TimeSpan? PlayedDuration { get; set; }

        public int? FinishPosition { get; set; }

        public decimal? PrizeWinnings { get; set; }

        public decimal? BountyWinnings { get; set; }

        public bool LateRegistration { get; set; }
    }
}
