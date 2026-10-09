namespace PokerCoach.Domain.Tournaments;

/// <summary>One buy-in of the player in a tournament, as the summary reports it.</summary>
/// <param name="PrizeWinnings">Null when the summary prints no prize.</param>
/// <param name="BountyWinnings">Null when the summary prints no bounty.</param>
/// <param name="Rebuys">Rebuys bought during this entry (each costs the tournament's rebuy price).</param>
/// <param name="Addons">Add-ons bought during this entry.</param>
public sealed record EntryOutcome(int? FinishPosition, decimal? PrizeWinnings, decimal? BountyWinnings, int Rebuys = 0, int Addons = 0);

public enum TournamentResultStatus
{
    /// <summary>Buy-ins and winnings are known: the tournament counts in totals.</summary>
    Known,

    /// <summary>Hands imported without the summary file: entries and winnings are unknown.</summary>
    MissingSummary,

    /// <summary>A summary exists but something needed is not in it (finish position or buy-in).</summary>
    Incomplete,
}

/// <summary>Money result of the player in one tournament. Amounts are null unless the status is known.</summary>
/// <param name="PaidEntries">
/// Entries that won a prize (bounties excluded: the files do not give the paid places, so "in the money"
/// means a prize was printed).
/// </param>
public sealed record TournamentResult(
    TournamentResultStatus Status,
    int? Entries,
    decimal? TotalBuyIn,
    decimal? PrizeWinnings,
    decimal? BountyWinnings,
    decimal? Profit,
    int? PaidEntries = null)
{
    /// <summary>
    /// Profit = prize + bounties − money paid: buy-in × entries + rebuys × rebuy price + add-ons × add-on
    /// price (fees included: it is money the player paid).
    /// A summary that gives the finish position but prints no prize or bounty means none was won: zero.
    /// Without a summary nothing is assumed: the result is unknown, never zero.
    /// </summary>
    /// <param name="buyInPerEntry">Full price of one entry, fee included; null when unknown.</param>
    /// <param name="entries">Entries from the summary; empty when no summary was imported.</param>
    /// <param name="rebuyCost">Full price of one rebuy; null when the tournament has none (or unknown).</param>
    /// <param name="addonCost">Full price of one add-on; null when the tournament has none (or unknown).</param>
    public static TournamentResult Compute(
        decimal? buyInPerEntry,
        IReadOnlyList<EntryOutcome> entries,
        decimal? rebuyCost = null,
        decimal? addonCost = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
        {
            return Unknown(TournamentResultStatus.MissingSummary, null);
        }

        var rebuys = entries.Sum(e => e.Rebuys);
        var addons = entries.Sum(e => e.Addons);
        if (buyInPerEntry is not { } buyIn
            || entries.Any(e => e.FinishPosition is null && e.PrizeWinnings is null)
            || (rebuys > 0 && rebuyCost is null)
            || (addons > 0 && addonCost is null))
        {
            return Unknown(TournamentResultStatus.Incomplete, entries.Count);
        }

        var prize = entries.Sum(e => e.PrizeWinnings ?? 0m);
        var bounty = entries.Sum(e => e.BountyWinnings ?? 0m);
        var totalBuyIn = (buyIn * entries.Count) + (rebuys * (rebuyCost ?? 0m)) + (addons * (addonCost ?? 0m));
        return new TournamentResult(
            TournamentResultStatus.Known,
            entries.Count,
            totalBuyIn,
            prize,
            bounty,
            prize + bounty - totalBuyIn,
            entries.Count(e => e.PrizeWinnings > 0m));
    }

    private static TournamentResult Unknown(TournamentResultStatus status, int? entries) =>
        new(status, entries, null, null, null, null);
}
