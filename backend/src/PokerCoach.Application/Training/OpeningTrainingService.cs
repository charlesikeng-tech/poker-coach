using PokerCoach.Application.Leaks;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Training;

namespace PokerCoach.Application.Training;

/// <summary>One answer given in an open-or-fold drill, as stored.</summary>
public sealed record DrillAttempt(DrillItem Item, DrillAnswer Answer, bool Correct, DateTimeOffset AnsweredAt);

public interface ITrainingStore
{
    Task RecordAsync(Guid userId, DrillAttempt attempt, int referenceVersion, CancellationToken cancellationToken);

    /// <summary>The user's latest attempts for a format, most recent first.</summary>
    Task<IReadOnlyList<DrillAttempt>> RecentAsync(Guid userId, TableFormat format, int count, CancellationToken cancellationToken);
}

/// <param name="Review">The hand was missed before and is asked again.</param>
/// <param name="Focus">The seat was weighted up because a leak was detected there.</param>
public sealed record DrillSpot(OpeningSpot Spot, bool Review, bool Focus);

/// <param name="ReferenceNotation">Null for computed push/fold ranges.</param>
/// <param name="ReferenceHands">The reference range of the seat: shown with the answer.</param>
public sealed record DrillResult(DrillAnswer Expected, bool Correct, string? ReferenceNotation, IReadOnlySet<HandClass> ReferenceHands, int ReferenceVersion);

public sealed record SeatProgress(PokerPosition Position, int Attempts, int Correct);

/// <param name="Streak">Correct answers in a row, latest first.</param>
/// <param name="DueReviews">Missed hands not yet answered right since.</param>
public sealed record DrillProgress(int Attempts, int Correct, int Streak, int DueReviews, IReadOnlyList<SeatProgress> BySeat);

/// <summary>
/// The open-or-fold trainer: deals spots (missed hands come back, seats with a detected opening leak come
/// more often), checks answers against the reference ranges, keeps the player's record.
/// </summary>
public sealed class OpeningTrainingService(ITrainingStore store, LeakService leaks, TimeProvider time, Random random)
{
    /// <summary>Attempts looked at for reviews and progress: recent work matters, old work fades.</summary>
    internal const int History = 500;

    /// <summary>Share of spots that replay a missed hand when there is one.</summary>
    internal const double ReviewShare = 0.3;

    /// <summary>Weight of a seat with a detected opening leak, against 1 for the others.</summary>
    internal const int FocusWeight = 3;

    /// <param name="positions">Seats to train; empty: every seat that opens at the format.</param>
    public async Task<DrillSpot> NextAsync(
        Guid userId,
        TableFormat format,
        StackBand band,
        IReadOnlyCollection<PokerPosition> positions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(positions);
        var allowed = ReferenceOpeningRanges.Positions(format)
            .Where(p => positions.Count == 0 || positions.Contains(p))
            .ToList();
        if (allowed.Count == 0)
        {
            allowed = [.. ReferenceOpeningRanges.Positions(format)];
        }

        var recent = await store.RecentAsync(userId, format, History, cancellationToken);
        var due = Due(recent).Where(i => i.Band == band && allowed.Contains(i.Position)).ToList();
        if (due.Count > 0 && random.NextDouble() < ReviewShare)
        {
            return new DrillSpot(OpeningDrill.Deal(random, format, band, allowed, due[random.Next(due.Count)]), Review: true, Focus: false);
        }

        var analysis = await leaks.GetAsync(userId, format, null, null, cancellationToken);
        var leaking = analysis.Report.Leaks
            .Where(l => l.Stat == LeakStat.Rfi && l.Position is { } p && allowed.Contains(p))
            .Select(l => l.Position!.Value)
            .ToHashSet();
        var weighted = allowed.SelectMany(p => Enumerable.Repeat(p, leaking.Contains(p) ? FocusWeight : 1)).ToList();
        var spot = OpeningDrill.Deal(random, format, band, weighted);
        return new DrillSpot(spot, Review: false, Focus: leaking.Contains(spot.Item.Position));
    }

    public async Task<DrillResult> AnswerAsync(Guid userId, DrillItem item, DrillAnswer answer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var expected = OpeningDrill.Expected(item);
        var correct = expected == answer;
        await store.RecordAsync(userId, new DrillAttempt(item, answer, correct, time.GetUtcNow()), ReferenceOpeningRanges.Version, cancellationToken);
        return new DrillResult(
            expected,
            correct,
            ReferenceOpeningRanges.NotationFor(item.Band, item.Format, item.Position),
            ReferenceOpeningRanges.For(item.Band, item.Format, item.Position, item.PushStack)!,
            ReferenceOpeningRanges.Version);
    }

    public async Task<DrillProgress> ProgressAsync(Guid userId, TableFormat format, CancellationToken cancellationToken)
    {
        var recent = await store.RecentAsync(userId, format, History, cancellationToken);
        var streak = recent.TakeWhile(a => a.Correct).Count();
        var bySeat = ReferenceOpeningRanges.Positions(format)
            .Select(p => new SeatProgress(p, recent.Count(a => a.Item.Position == p), recent.Count(a => a.Item.Position == p && a.Correct)))
            .ToList();
        return new DrillProgress(recent.Count, recent.Count(a => a.Correct), streak, Due(recent).Count, bySeat);
    }

    /// <summary>Items whose latest answer was wrong: they come back until answered right.</summary>
    internal static List<DrillItem> Due(IReadOnlyList<DrillAttempt> recentFirst) =>
        recentFirst
            .GroupBy(a => a.Item)
            .Where(g => !g.First().Correct)
            .Select(g => g.Key)
            .ToList();
}
