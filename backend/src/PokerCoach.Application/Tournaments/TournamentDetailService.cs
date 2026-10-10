using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Application.Tournaments;

/// <summary>One of the hero's hands in a tournament, in play order.</summary>
/// <param name="HeroStack">Hero's chips at the start of the hand; null when the hero was not dealt in.</param>
/// <param name="Facts">Null while the facts are being computed (or recomputed after a definition change).</param>
public sealed record TournamentHandRow(
    Guid HandId,
    DateTimeOffset StartedAt,
    int Level,
    long BigBlind,
    long? HeroStack,
    string? HeroCards,
    HeroHandFacts? Facts);

public interface ITournamentDetailStore
{
    /// <summary>The tournament if it belongs to one of the user's confirmed poker accounts; otherwise null.</summary>
    Task<TournamentFacts?> FindAsync(Guid userId, Guid tournamentId, CancellationToken cancellationToken);

    /// <summary>The hero's hands of the tournament in play order, with facts of <paramref name="factsVersion"/> only.</summary>
    Task<IReadOnlyList<TournamentHandRow>> GetHandsAsync(Guid tournamentId, int factsVersion, CancellationToken cancellationToken);
}

/// <param name="Index">Position of the hand in the tournament, from 1.</param>
/// <param name="Stack">Hero's chips at the start of the hand.</param>
public sealed record StackPoint(int Index, DateTimeOffset StartedAt, int Level, long Stack, decimal StackInBigBlinds);

/// <summary>A key moment with what the page needs to name the hand.</summary>
/// <param name="AllInEquity">Preflop all-in with every hand shown: the hero's computed share of the main pot.</param>
public sealed record KeyMomentDetail(
    KeyMoment Moment,
    Guid HandId,
    DateTimeOffset StartedAt,
    int Level,
    PokerPosition? Position,
    string? HeroCards,
    decimal StackInBigBlinds,
    decimal? AllInEquity = null);

/// <param name="HandsDuration">From the first to the last imported hand; null with fewer than two hands.</param>
/// <param name="Stats">Over hands with computed facts only. One tournament is a small sample: rates are descriptive, never leaks.</param>
/// <param name="PendingHands">Hands whose facts are still being computed: stats and key moments are partial until 0.</param>
public sealed record TournamentDetail(
    TournamentListItem Tournament,
    string? Type,
    string? Speed,
    TimeSpan? HandsDuration,
    IReadOnlyList<StackPoint> Stack,
    IReadOnlyList<KeyMomentDetail> KeyMoments,
    StatLine Stats,
    int PendingHands);

/// <summary>The detail of one tournament: result, stack over time, the hands that decided it, session stats.</summary>
public sealed class TournamentDetailService(ITournamentDetailStore store)
{
    public async Task<TournamentDetail?> GetAsync(Guid userId, Guid tournamentId, CancellationToken cancellationToken)
    {
        var facts = await store.FindAsync(userId, tournamentId, cancellationToken);
        if (facts is null)
        {
            return null;
        }

        var hands = await store.GetHandsAsync(tournamentId, HeroHandFacts.Version, cancellationToken);
        var dealt = hands
            .Where(h => h.HeroStack is > 0 && h.BigBlind > 0)
            .Select((h, i) => (Index: i + 1, Hand: h, Stack: h.HeroStack!.Value))
            .ToList();

        var stack = dealt
            .Select(d => new StackPoint(d.Index, d.Hand.StartedAt, d.Hand.Level, d.Stack, Math.Round((decimal)d.Stack / d.Hand.BigBlind, 1)))
            .ToList();

        var byIndex = dealt.ToDictionary(d => d.Index);
        var moments = Domain.Tournaments.KeyMoments
            .Select(dealt
                .Where(d => d.Hand.Facts is not null)
                .Select(d => new KeyMomentCandidate(
                    d.Index,
                    d.Stack,
                    d.Hand.Facts!.NetChips,
                    d.Hand.Facts.NetBigBlinds,
                    d.Hand.Facts.SawFlop,
                    d.Hand.Facts.WentToShowdown)))
            .Select(m =>
            {
                var (_, hand, _) = byIndex[m.Index];
                return new KeyMomentDetail(m, hand.HandId, hand.StartedAt, hand.Level, hand.Facts!.Position, hand.HeroCards, hand.Facts.StackInBigBlinds, hand.Facts.AllInEquity);
            })
            .ToList();

        var counts = hands
            .Where(h => h.Facts is not null)
            .Aggregate(HeroStatCounts.Zero, (total, h) => total.Add(HeroStatCounts.Of(h.Facts!)));

        return new TournamentDetail(
            TournamentListService.ToItem(facts),
            facts.Type,
            facts.Speed,
            hands.Count < 2 ? null : hands[^1].StartedAt - hands[0].StartedAt,
            stack,
            moments,
            StatLine.From(counts),
            hands.Count(h => h.Facts is null));
    }
}
