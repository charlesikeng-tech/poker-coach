using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Application.Tournaments;

/// <summary>Raw facts of one tournament, as stored. Buy-in parts are null when their source was not imported.</summary>
/// <param name="Entries">Empty when no summary was imported.</param>
public sealed record TournamentFacts(
    Guid Id,
    string Name,
    DateTimeOffset? StartedAt,
    string? Currency,
    decimal? PrizePoolBuyIn,
    decimal? BountyBuyIn,
    decimal? BuyInExcludingFee,
    decimal? Fee,
    int? RegisteredPlayers,
    int HandCount,
    IReadOnlyList<EntryOutcome> Entries,
    string? Type = null,
    string? Speed = null,
    TournamentCoverage? Coverage = null);

public interface ITournamentReadStore
{
    /// <summary>
    /// Tournaments of the user's confirmed poker accounts only (ADR-0005: an unconfirmed pseudonym does
    /// not count), started within the optional bounds, most recent first.
    /// </summary>
    Task<IReadOnlyList<TournamentFacts>> ListAsync(Guid userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
}

/// <param name="To">Exclusive upper bound.</param>
public sealed record TournamentFilter(DateTimeOffset? From, DateTimeOffset? To, decimal? MinBuyIn, decimal? MaxBuyIn, int Page, int PageSize);

/// <param name="BuyIn">Price of one entry, fee included; null when unknown.</param>
/// <param name="FinishPosition">Position of the last entry.</param>
/// <param name="Coverage">Null while it is being computed.</param>
public sealed record TournamentListItem(
    Guid Id,
    string Name,
    DateTimeOffset? StartedAt,
    string? Currency,
    decimal? BuyIn,
    int? RegisteredPlayers,
    int? FinishPosition,
    int HandCount,
    TournamentResult Result,
    TournamentCoverage? Coverage = null);

public sealed record TournamentPage(IReadOnlyList<TournamentListItem> Items, int Page, int PageSize, int TotalCount, PerformanceFigures Totals);

/// <summary>
/// The tournaments screen: list, filters and totals. Results are computed in memory over all filtered
/// tournaments (totals need them all anyway). A player has thousands of tournaments, not millions:
/// move the aggregation to SQL when a profile shows it matters.
/// </summary>
public sealed class TournamentListService(ITournamentReadStore store)
{
    public async Task<TournamentPage> ListAsync(Guid userId, TournamentFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var facts = await store.ListAsync(userId, filter.From, filter.To, cancellationToken);
        var items = facts
            .Select(ToItem)
            .Where(i => filter.MinBuyIn is null || (i.BuyIn is { } b && b >= filter.MinBuyIn))
            .Where(i => filter.MaxBuyIn is null || (i.BuyIn is { } b && b <= filter.MaxBuyIn))
            .ToList();

        var page = items.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();
        return new TournamentPage(page, filter.Page, filter.PageSize, items.Count, PerformanceFigures.Of(items.Select(i => i.Result).ToList()));
    }

    internal static TournamentListItem ToItem(TournamentFacts facts)
    {
        var buyIn = BuyInPerEntry(facts);
        return new TournamentListItem(
            facts.Id,
            facts.Name,
            facts.StartedAt,
            facts.Currency,
            buyIn,
            facts.RegisteredPlayers,
            facts.Entries.Count == 0 ? null : facts.Entries[^1].FinishPosition,
            facts.HandCount,
            TournamentResult.Compute(buyIn, facts.Entries),
            facts.Coverage);
    }

    /// <summary>The summary splits the buy-in (prize pool + bounty + fee); hands only give it without the fee, plus the fee.</summary>
    internal static decimal? BuyInPerEntry(TournamentFacts facts) =>
        facts.PrizePoolBuyIn is { } prizePool && facts.Fee is { } fee
            ? prizePool + (facts.BountyBuyIn ?? 0m) + fee
            : facts.BuyInExcludingFee is { } excludingFee && facts.Fee is { } handFee
                ? excludingFee + handFee
                : null;
}
