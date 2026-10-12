using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Training;

namespace PokerCoach.Application.Training;

/// <summary>One answer given in a sizing drill, as stored.</summary>
public sealed record SizingAttempt(SizingItem Item, RaiseSize Answer, bool Correct, DateTimeOffset AnsweredAt);

public interface ISizingTrainingStore
{
    Task RecordAsync(Guid userId, SizingAttempt attempt, int referenceVersion, CancellationToken cancellationToken);

    /// <summary>The user's latest sizing attempts at a format checked against the current reference, most recent first.</summary>
    Task<IReadOnlyList<SizingAttempt>> RecentAsync(Guid userId, TableFormat format, int count, CancellationToken cancellationToken);
}

/// <summary>A raise the hero made preflop with its context (current facts): an open, or a 3-bet against a single open.</summary>
/// <param name="Position">As <see cref="PositionResolver"/> named it in the hand.</param>
/// <param name="OpenToBigBlinds">Set for an open; <paramref name="ThreeBetToBigBlinds"/> for a 3-bet.</param>
public sealed record PlayedRaiseRow(
    Guid HandId,
    DateTimeOffset PlayedAt,
    PokerPosition Position,
    int PlayersDealt,
    decimal StackBigBlinds,
    decimal? OpenToBigBlinds,
    bool OpenAllIn,
    PokerPosition? Opener,
    decimal? FacingOpenToBigBlinds,
    decimal? EffectiveStackBigBlinds,
    decimal? ThreeBetToBigBlinds,
    bool ThreeBetAllIn);

public interface IPlayedSizingStore
{
    Task<IReadOnlyList<PlayedRaiseRow>> ListRaisesAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken);
}

/// <param name="Choices">Sizes offered; one of them at least is inside the reference interval.</param>
public sealed record SizingDrillSpot(SizingSpot Spot, IReadOnlyList<RaiseSize> Choices, bool Review);

public sealed record SizingDrillResult(bool Correct, SizingVerdict Verdict, SizingAdvice Advice, bool InPosition, int ReferenceVersion);

/// <param name="Streak">Correct answers in a row, latest first.</param>
/// <param name="DueReviews">Missed spots not yet answered right since.</param>
public sealed record SizingProgress(int Attempts, int Correct, int Streak, int DueReviews);

/// <summary>
/// The player's own raises of one kind, seat (opens) or side (3-bets: in or out of position) and stack band, judged
/// against the reference hand by hand (each hand with its own stack and open faced).
/// </summary>
/// <param name="Seat">Opens: the seat, named by distance to the button for the format. Null for 3-bets.</param>
/// <param name="InPosition">3-bets: whether the hero was in position. Null for opens.</param>
/// <param name="TypicalSize">Median of the sizes that were not all-in (big blinds for opens, multiples for 3-bets); null if none.</param>
/// <param name="Reference">The reference size most hands of the group called for.</param>
/// <param name="OffHands">The most recent hands off the reference, to replay.</param>
public sealed record PlayedSizingGroup(
    SizingKind Kind,
    PokerPosition? Seat,
    bool? InPosition,
    StackBand Band,
    int Hands,
    int InLine,
    int TooSmall,
    int TooBig,
    int AllIns,
    decimal? TypicalSize,
    RaiseSize Reference,
    IReadOnlyList<Guid> OffHands);

public sealed record PlayedSizings(TableFormat Format, IReadOnlyList<PlayedSizingGroup> Groups, int PendingHands, int ReferenceVersion);

/// <summary>
/// Sizing (ADR-0020): drills on how much to raise, checked against <see cref="ReferenceSizings"/>, and the player's
/// own open and 3-bet sizes next to the same reference. Missed spots come back, as in the other drills.
/// </summary>
public sealed class SizingTrainingService(
    ISizingTrainingStore store,
    IPlayedSizingStore played,
    IStatisticsReadStore statistics,
    TimeProvider time,
    Random random)
{
    internal const int History = 500;

    internal const double ReviewShare = 0.3;

    internal const int OffHandsShown = 3;

    /// <param name="kind">Open or 3-bet; null for both.</param>
    public async Task<SizingDrillSpot> NextAsync(Guid userId, TableFormat format, StackBand band, SizingKind? kind, CancellationToken cancellationToken)
    {
        var recent = await store.RecentAsync(userId, format, History, cancellationToken);
        var due = Due(recent).Where(i => i.Band == band && (kind is null || i.Kind == kind)).ToList();
        var review = due.Count > 0 && random.NextDouble() < ReviewShare ? due[random.Next(due.Count)] : null;
        var spot = SizingDrill.Deal(random, format, band, kind, review);
        return new SizingDrillSpot(spot, SizingDrill.Choices(spot.Item), review is not null);
    }

    /// <exception cref="ArgumentException">The answer is not one of the sizes offered for the spot.</exception>
    public async Task<SizingDrillResult> AnswerAsync(Guid userId, SizingItem item, RaiseSize answer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(answer);
        if (!SizingDrill.IsOffered(item, answer))
        {
            throw new ArgumentException("Not a size offered for this spot.", nameof(answer));
        }

        var advice = SizingDrill.Advice(item);
        var verdict = advice.Judge(answer);
        var correct = verdict == SizingVerdict.InLine;
        await store.RecordAsync(userId, new SizingAttempt(item, answer, correct, time.GetUtcNow()), ReferenceSizings.Version, cancellationToken);
        var inPosition = item.Opener is { } opener && ReferenceSizings.InPosition(item.Position, opener);
        return new SizingDrillResult(correct, verdict, advice, inPosition, ReferenceSizings.Version);
    }

    public async Task<SizingProgress> ProgressAsync(Guid userId, TableFormat format, CancellationToken cancellationToken)
    {
        var recent = await store.RecentAsync(userId, format, History, cancellationToken);
        return new SizingProgress(recent.Count, recent.Count(a => a.Correct), recent.TakeWhile(a => a.Correct).Count(), Due(recent).Count);
    }

    /// <summary>The player's raises at a format over a period, grouped and judged (see <see cref="PlayedSizingGroup"/>).</summary>
    public async Task<PlayedSizings> PlayedAsync(Guid userId, TableFormat format, DateTimeOffset? from, CancellationToken cancellationToken)
    {
        var rows = await played.ListRaisesAsync(userId, new StatisticsFilter(from, null, null, null, Format: format), HeroHandFacts.Version, cancellationToken);
        var pending = await statistics.CountPendingAsync(userId, HeroHandFacts.Version, cancellationToken);

        var judged = rows.Select(r => Judge(r, format)).OfType<JudgedRaise>().ToList();
        var groups = judged
            .GroupBy(j => (j.Kind, j.Seat, j.InPosition, j.Band))
            .Select(g =>
            {
                var sizes = g.Where(j => !j.Played.IsAllIn).Select(j => j.Played.Value!.Value).Order().ToList();
                return new PlayedSizingGroup(
                    g.Key.Kind,
                    g.Key.Seat,
                    g.Key.InPosition,
                    g.Key.Band,
                    g.Count(),
                    g.Count(j => j.Verdict == SizingVerdict.InLine),
                    g.Count(j => j.Verdict == SizingVerdict.TooSmall),
                    g.Count(j => j.Verdict == SizingVerdict.TooBig),
                    g.Count(j => j.Played.IsAllIn),
                    Median(sizes),
                    g.GroupBy(j => j.Reference).OrderByDescending(r => r.Count()).First().Key,
                    [.. g.Where(j => j.Verdict != SizingVerdict.InLine).OrderByDescending(j => j.PlayedAt).Take(OffHandsShown).Select(j => j.HandId)]);
            })
            .OrderBy(g => g.Kind)
            // Shortest stacks first, as the bands read.
            .ThenBy(g => ReferenceOpeningRanges.Bounds(g.Band).Min)
            .ThenBy(g => g.Seat is { } seat ? SeatOrder(seat, format) : 0)
            .ThenByDescending(g => g.InPosition)
            .ToList();

        return new PlayedSizings(format, groups, pending, ReferenceSizings.Version);
    }

    /// <summary>Spots whose latest answer was wrong: they come back until answered right.</summary>
    internal static List<SizingItem> Due(IReadOnlyList<SizingAttempt> recentFirst) =>
        recentFirst
            .GroupBy(a => a.Item)
            .Where(g => !g.First().Correct)
            .Select(g => g.Key)
            .ToList();

    private static JudgedRaise? Judge(PlayedRaiseRow row, TableFormat format)
    {
        if (row.OpenToBigBlinds is { } openTo)
        {
            var played = row.OpenAllIn ? RaiseSize.AllIn : RaiseSize.Of(openTo);
            var advice = ReferenceSizings.Open(row.Position, row.StackBigBlinds);
            return new JudgedRaise(
                row.HandId,
                row.PlayedAt,
                SizingKind.Open,
                OpeningSeat.Canonical(row.Position, row.PlayersDealt, format),
                null,
                RealSpots.BandOf(row.StackBigBlinds),
                played,
                advice.Best,
                advice.Judge(played));
        }

        if (row is { ThreeBetToBigBlinds: { } threeBetTo, Opener: { } opener, FacingOpenToBigBlinds: { } faced and > 0, EffectiveStackBigBlinds: { } effective })
        {
            var played = row.ThreeBetAllIn ? RaiseSize.AllIn : RaiseSize.Of(Math.Round(threeBetTo / faced, 2));
            var advice = ReferenceSizings.ThreeBet(row.Position, opener, faced, effective);
            return new JudgedRaise(
                row.HandId,
                row.PlayedAt,
                SizingKind.ThreeBet,
                null,
                ReferenceSizings.InPosition(row.Position, opener),
                RealSpots.BandOf(effective),
                played,
                advice.Best,
                advice.Judge(played));
        }

        return null;
    }

    private static int SeatOrder(PokerPosition seat, TableFormat format)
    {
        var index = ReferenceOpeningRanges.Positions(format).ToList().IndexOf(seat);
        return index < 0 ? int.MaxValue : index;
    }

    private static decimal? Median(IReadOnlyList<decimal> sorted) =>
        sorted.Count == 0
            ? null
            : sorted.Count % 2 == 1
                ? sorted[sorted.Count / 2]
                : Math.Round((sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2, 2, MidpointRounding.AwayFromZero);

    private sealed record JudgedRaise(
        Guid HandId,
        DateTimeOffset PlayedAt,
        SizingKind Kind,
        PokerPosition? Seat,
        bool? InPosition,
        StackBand Band,
        RaiseSize Played,
        RaiseSize Reference,
        SizingVerdict Verdict);
}
