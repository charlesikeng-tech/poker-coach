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

        // A re-entry appends a complete block (header included) to the same file: one block per entry.
        var lines = WinamaxValues.SplitLines(content);
        var blocks = new List<SummaryFields>();
        SummaryFields? current = null;

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

            var header = WinamaxPatterns.SummaryHeader().Match(text);
            if (header.Success)
            {
                current = new SummaryFields
                {
                    HeaderLine = lineNumber,
                    TournamentName = header.Groups["name"].Value.Trim(),
                    TournamentId = header.Groups["id"].Value,
                    LateRegistration = header.Groups["late"].Success,
                };
                blocks.Add(current);
                continue;
            }

            if (current is null)
            {
                return Failure(ParseErrorCodes.UnrecognizedFormat, lineNumber);
            }

            var code = ReadLine(text, current);
            if (code is not null)
            {
                return Failure(code, lineNumber);
            }
        }

        if (blocks.Count == 0)
        {
            return Failure(ParseErrorCodes.EmptyFile, 1);
        }

        foreach (var block in blocks)
        {
            if (!block.HasMandatoryFields)
            {
                return Failure(ParseErrorCodes.MissingField, block.HeaderLine);
            }
        }

        // Every block must describe the same tournament, player and buy-in: anything else is not a
        // re-entry we understand, and mixing it would corrupt ROI.
        var first = blocks[0];
        foreach (var block in blocks.Skip(1))
        {
            if (block.TournamentId != first.TournamentId || block.TournamentName != first.TournamentName
                || block.PlayerName != first.PlayerName || block.PrizePoolBuyIn != first.PrizePoolBuyIn
                || block.BountyBuyIn != first.BountyBuyIn || block.Fee != first.Fee)
            {
                return Failure(ParseErrorCodes.UnexpectedSection, block.HeaderLine);
            }
        }

        var last = blocks[^1];
        var summary = new ParsedTournamentSummary
        {
            Room = PokerRoom.Winamax,
            ExternalTournamentId = last.TournamentId!,
            TournamentName = last.TournamentName!,
            PlayerName = last.PlayerName!,
            PrizePoolBuyIn = last.PrizePoolBuyIn!.Value,
            BountyBuyIn = last.BountyBuyIn,
            Fee = last.Fee!.Value,
            Currency = WinamaxValues.Currency,
            RegisteredPlayers = last.RegisteredPlayers!.Value,
            Mode = last.Mode,
            Type = last.Type,
            Speed = last.Speed,
            FlightId = last.FlightId,
            PrizePool = last.PrizePool!.Value,
            StartedAt = last.StartedAt!.Value,
            RebuyCost = last.RebuyCost,
            AddonCost = last.AddonCost,
            TotalRebuys = last.TotalRebuys,
            TotalAddons = last.TotalAddons,
            Entries = blocks
                .Select((block, index) => new ParsedTournamentEntry(
                    index + 1,
                    block.LateRegistration,
                    block.PlayedDuration,
                    block.FinishPosition,
                    block.PrizeWinnings,
                    block.BountyWinnings,
                    block.Rebuys ?? 0,
                    block.Addons ?? 0))
                .ToList(),
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
            // Without a prize part (bounties only), the prize stays unknown: not printed is not zero here.
            if (won.Groups["prize"].Success)
            {
                if (!WinamaxValues.TryParseEuros(won.Groups["prize"].Value, out var prize))
                {
                    return ParseErrorCodes.InvalidNumber;
                }

                fields.PrizeWinnings = prize;
            }

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
            case "Rebuy cost":
                return ReadCost(value, cost => fields.RebuyCost = cost);
            case "Addon cost":
                return ReadCost(value, cost => fields.AddonCost = cost);
            case "Your rebuys":
                return ReadCount(value, count => fields.Rebuys = count);
            case "Your addons":
                return ReadCount(value, count => fields.Addons = count);
            case "Total rebuys":
                return ReadCount(value, count => fields.TotalRebuys = count);
            case "Total addons":
                return ReadCount(value, count => fields.TotalAddons = count);
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

    /// <summary>"4€ + 1€": the full price of one rebuy or add-on (prize pool part + fee).</summary>
    private static string? ReadCost(string value, Action<decimal> set)
    {
        var total = 0m;
        foreach (var part in value.Split(" + "))
        {
            if (!WinamaxValues.TryParseEurosWithSign(part, out var amount))
            {
                return ParseErrorCodes.UnsupportedBuyIn;
            }

            total += amount;
        }

        set(total);
        return null;
    }

    private static string? ReadCount(string value, Action<int> set)
    {
        if (!WinamaxValues.TryParseInt(value, out var count))
        {
            return ParseErrorCodes.InvalidNumber;
        }

        set(count);
        return null;
    }

    private static TournamentSummaryParseResult Failure(string code, int lineNumber) =>
        new(null, [new ParseError(code, lineNumber, null)]);

    private sealed class SummaryFields
    {
        public int HeaderLine { get; init; }

        public bool HasMandatoryFields =>
            TournamentId is not null && TournamentName is not null && PlayerName is not null
            && PrizePoolBuyIn is not null && Fee is not null && RegisteredPlayers is not null
            && PrizePool is not null && StartedAt is not null;

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

        public decimal? RebuyCost { get; set; }

        public decimal? AddonCost { get; set; }

        public int? Rebuys { get; set; }

        public int? Addons { get; set; }

        public int? TotalRebuys { get; set; }

        public int? TotalAddons { get; set; }
    }
}
