using PokerCoach.Application.Leaks;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Training;

namespace PokerCoach.Application.Training;

/// <summary>One answer given in a drill, as stored.</summary>
/// <param name="SourceHandId">Quiz on real hands: the hand the spot comes from; null for dealt spots.</param>
public sealed record DrillAttempt(DrillItem Item, DrillAnswer Answer, bool Correct, DateTimeOffset AnsweredAt, Guid? SourceHandId = null);

/// <summary>The hero's real raise-first-in spots (current facts), for the quiz on his own hands.</summary>
public interface IRealSpotStore
{
    Task<IReadOnlyList<RealOpeningRow>> ListOpeningSpotsAsync(Guid userId, TableFormat format, DateTimeOffset since, int factsVersion, CancellationToken cancellationToken);
}

/// <param name="Actual">What the hero did that day.</param>
/// <param name="Remaining">Missed real spots left to fix, this one included.</param>
public sealed record RealDrill(DrillSpot Drill, Guid HandId, DateTimeOffset PlayedAt, RealAction Actual, int Remaining);

/// <summary>Which drill: open or fold when folded to, or call or fold a shove from the big blind.</summary>
public enum DrillMode
{
    Open,
    Defence,
}

public interface ITrainingStore
{
    Task RecordAsync(Guid userId, DrillAttempt attempt, int referenceVersion, CancellationToken cancellationToken);

    /// <summary>Real hands already asked in the quiz, with whether the latest answer was right.</summary>
    Task<IReadOnlyDictionary<Guid, bool>> RealHandOutcomesAsync(Guid userId, TableFormat format, CancellationToken cancellationToken);

    /// <summary>Answers given from a moment (until another, exclusive, when set), every format and drill: the weekly plan's goal.</summary>
    Task<(int Attempts, int Correct)> CountSinceAsync(Guid userId, DateTimeOffset since, DateTimeOffset? until, CancellationToken cancellationToken);

    /// <summary>The user's latest attempts for a format and drill, most recent first.</summary>
    Task<IReadOnlyList<DrillAttempt>> RecentAsync(Guid userId, TableFormat format, DrillMode mode, int count, CancellationToken cancellationToken);
}

/// <param name="Review">The hand was missed before and is asked again.</param>
/// <param name="Focus">The seat was weighted up because a leak was detected there.</param>
public sealed record DrillSpot(OpeningSpot Spot, bool Review, bool Focus);

/// <param name="ReferenceNotation">Null for computed push/fold ranges.</param>
/// <param name="ReferenceHands">The reference range of the seat (its call range in defence drills): shown with the answer.</param>
public sealed record DrillResult(DrillAnswer Expected, bool Correct, string? ReferenceNotation, IReadOnlySet<HandClass> ReferenceHands, int ReferenceVersion);

/// <param name="Position">The hero's seat in opening drills; the shover's in defence drills.</param>
public sealed record SeatProgress(PokerPosition Position, int Attempts, int Correct);

/// <param name="Streak">Correct answers in a row, latest first.</param>
/// <param name="DueReviews">Missed hands not yet answered right since.</param>
public sealed record DrillProgress(int Attempts, int Correct, int Streak, int DueReviews, IReadOnlyList<SeatProgress> BySeat);

/// <summary>
/// The open-or-fold trainer: deals spots (missed hands come back, seats with a detected opening leak come
/// more often), checks answers against the reference ranges, keeps the player's record.
/// </summary>
public sealed class OpeningTrainingService(ITrainingStore store, LeakService leaks, TimeProvider time, Random random, IRealSpotStore? realSpots = null)
{
    /// <summary>Real spots looked at: recent play, the player's current habits.</summary>
    public const int RealSpotDays = 90;

    /// <summary>The quiz draws among the most recent missed spots: fresh memories, current habits.</summary>
    internal const int RealSpotPool = 30;
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

        var recent = await store.RecentAsync(userId, format, DrillMode.Open, History, cancellationToken);
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

    /// <param name="shovers">Seats to defend against; empty: every seat that can shove into the big blind.</param>
    public async Task<DrillSpot> NextDefenceAsync(
        Guid userId,
        TableFormat format,
        IReadOnlyCollection<PokerPosition> shovers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shovers);
        var allowed = DefenceDrill.Shovers(format).Where(p => shovers.Count == 0 || shovers.Contains(p)).ToList();
        if (allowed.Count == 0)
        {
            allowed = [.. DefenceDrill.Shovers(format)];
        }

        var recent = await store.RecentAsync(userId, format, DrillMode.Defence, History, cancellationToken);
        var due = Due(recent).Where(i => i.Shover is { } s && allowed.Contains(s)).ToList();
        if (due.Count > 0 && random.NextDouble() < ReviewShare)
        {
            return new DrillSpot(DefenceDrill.Deal(random, format, allowed, due[random.Next(due.Count)]), Review: true, Focus: false);
        }

        return new DrillSpot(DefenceDrill.Deal(random, format, allowed), Review: false, Focus: false);
    }

    /// <summary>
    /// A spot from the player's own hands where his decision differed from the answer key, not yet fixed
    /// (answered right in the quiz). Null when there is none left: well played.
    /// </summary>
    public async Task<RealDrill?> NextRealAsync(Guid userId, TableFormat format, CancellationToken cancellationToken)
    {
        if (realSpots is null)
        {
            return null;
        }

        var since = time.GetUtcNow().AddDays(-RealSpotDays);
        var rows = await realSpots.ListOpeningSpotsAsync(userId, format, since, HeroHandFacts.Version, cancellationToken);
        var outcomes = await store.RealHandOutcomesAsync(userId, format, cancellationToken);
        var missed = rows
            .Select(r => RealSpots.From(r, format))
            .OfType<RealSpot>()
            .Where(s => s.Missed && !outcomes.GetValueOrDefault(s.HandId))
            .OrderByDescending(s => s.PlayedAt)
            .ToList();
        if (missed.Count == 0)
        {
            return null;
        }

        var pick = missed[random.Next(Math.Min(missed.Count, RealSpotPool))];
        return new RealDrill(
            new DrillSpot(pick.Spot, Review: outcomes.ContainsKey(pick.HandId), Focus: false),
            pick.HandId,
            pick.PlayedAt,
            pick.Actual,
            missed.Count);
    }

    /// <param name="sourceHandId">Set when the spot comes from the quiz on real hands.</param>
    public async Task<DrillResult> AnswerAsync(Guid userId, DrillItem item, DrillAnswer answer, CancellationToken cancellationToken, Guid? sourceHandId = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        var expected = OpeningDrill.Expected(item);
        var correct = expected == answer;
        await store.RecordAsync(userId, new DrillAttempt(item, answer, correct, time.GetUtcNow(), sourceHandId), ReferenceOpeningRanges.Version, cancellationToken);
        return item.IsDefence
            ? new DrillResult(expected, correct, null, DefenceDrill.Range(item), ReferenceOpeningRanges.Version)
            : new DrillResult(
                expected,
                correct,
                ReferenceOpeningRanges.NotationFor(item.Band, item.Format, item.Position),
                ReferenceOpeningRanges.For(item.Band, item.Format, item.Position, item.PushStack)!,
                ReferenceOpeningRanges.Version);
    }

    public async Task<DrillProgress> ProgressAsync(Guid userId, TableFormat format, DrillMode mode, CancellationToken cancellationToken)
    {
        var recent = await store.RecentAsync(userId, format, mode, History, cancellationToken);
        var streak = recent.TakeWhile(a => a.Correct).Count();

        // Opening drills are told apart by the hero's seat, defence drills by the shover's.
        Func<DrillAttempt, PokerPosition?> seat = mode == DrillMode.Defence ? a => a.Item.Shover : a => a.Item.Position;
        var seats = mode == DrillMode.Defence ? DefenceDrill.Shovers(format) : ReferenceOpeningRanges.Positions(format);
        var bySeat = seats
            .Select(p => new SeatProgress(p, recent.Count(a => seat(a) == p), recent.Count(a => seat(a) == p && a.Correct)))
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
