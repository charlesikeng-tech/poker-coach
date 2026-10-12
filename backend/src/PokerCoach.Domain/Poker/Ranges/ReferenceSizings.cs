using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Domain.Poker.Ranges;

/// <summary>Which raise is sized: an open in big blinds, a 3-bet in multiples of the open it faces.</summary>
public enum SizingKind
{
    Open,
    ThreeBet,
}

/// <summary>A raise as answered or played: its size (big blinds for an open, a multiple of the open for a 3-bet), or all-in.</summary>
public sealed record RaiseSize(decimal? Value)
{
    public static RaiseSize AllIn { get; } = new((decimal?)null);

    public bool IsAllIn => Value is null;

    public static RaiseSize Of(decimal value) => new(value);
}

/// <summary>A played size next to the reference.</summary>
public enum SizingVerdict
{
    InLine,
    TooSmall,
    TooBig,
}

/// <summary>
/// The reference size for a spot: the size to pick, and the interval that is still fine (same unit as the size).
/// All-in references accept only all-in.
/// </summary>
/// <param name="BestRaiseToBigBlinds">The best size as a raise-to in big blinds (a 3-bet's multiple applied); null when all-in.</param>
public sealed record SizingAdvice(SizingKind Kind, RaiseSize Best, decimal? Min, decimal? Max, decimal? BestRaiseToBigBlinds)
{
    /// <summary>
    /// Played sizes are rounded to a tenth first: a 900-chip open at 400/800 is 2.25 BB, which nobody means
    /// as different from 2.2 or 2.3.
    /// </summary>
    public SizingVerdict Judge(RaiseSize played)
    {
        ArgumentNullException.ThrowIfNull(played);
        if (Best.IsAllIn)
        {
            return played.IsAllIn ? SizingVerdict.InLine : SizingVerdict.TooSmall;
        }

        if (played.IsAllIn)
        {
            return SizingVerdict.TooBig;
        }

        var value = Math.Round(played.Value!.Value, 1, MidpointRounding.AwayFromZero);
        return value < Min ? SizingVerdict.TooSmall : value > Max ? SizingVerdict.TooBig : SizingVerdict.InLine;
    }
}

/// <summary>
/// Reference preflop sizes, version 1 (ADR-0020): our conventions for low-stakes online MTTs with antes, chip EV,
/// no ICM, like the reference ranges (ADR-0009, ADR-0018): references, never "GTO". One size per spot whatever
/// the hand, so the size gives nothing away.
/// <list type="bullet">
/// <item>Open: all-in below 15 BB (push/fold); otherwise 2 BB below 25 BB, 2.2 BB above, up to 2.5 BB deep; the small
/// blind, out of position against one player, raises bigger (2.5 BB below 25 BB, 3 BB above).</item>
/// <item>3-bet against a single open: about 3× in position, 4× out of position; all-in below 25 BB effective, or
/// whenever that raise would put more than a third of the effective stack in: past that point the 3-bettor is
/// committed anyway, and the shove takes the most fold equity and leaves the opener no cheap flop.</item>
/// </list>
/// </summary>
public static class ReferenceSizings
{
    /// <summary>Bump on any change below: drill answers are checked against one version.</summary>
    public const int Version = 1;

    /// <summary>Below this stack, an open is a shove (the push/fold band).</summary>
    public const decimal ShoveOpenBelow = 15m;

    /// <summary>Below this effective stack, a 3-bet is a shove.</summary>
    public const decimal ShoveThreeBetBelow = 25m;

    /// <summary>A raise putting in more than this share of the effective stack commits: shove instead.</summary>
    public const decimal CommitShare = 1m / 3m;

    /// <summary>Sizes offered in the open drill.</summary>
    public static IReadOnlyList<RaiseSize> OpenChoices { get; } =
        [RaiseSize.Of(2m), RaiseSize.Of(2.2m), RaiseSize.Of(2.5m), RaiseSize.Of(3m), RaiseSize.AllIn];

    /// <summary>Sizes offered in the 3-bet drill, as multiples of the open.</summary>
    public static IReadOnlyList<RaiseSize> ThreeBetChoices { get; } =
        [RaiseSize.Of(2.5m), RaiseSize.Of(3m), RaiseSize.Of(3.5m), RaiseSize.Of(4m), RaiseSize.Of(4.5m), RaiseSize.AllIn];

    /// <summary>The open size for a seat (as <see cref="PositionResolver"/> names it) at a stack, folded to.</summary>
    public static SizingAdvice Open(PokerPosition seat, decimal stackBigBlinds)
    {
        if (stackBigBlinds < ShoveOpenBelow)
        {
            return new SizingAdvice(SizingKind.Open, RaiseSize.AllIn, null, null, null);
        }

        var (best, min, max) = (seat, stackBigBlinds < 25m, stackBigBlinds < 40m) switch
        {
            (PokerPosition.SmallBlind, true, _) => (2.5m, 2.5m, 3m),
            (PokerPosition.SmallBlind, false, _) => (3m, 2.5m, 3.5m),
            (_, true, _) => (2m, 2m, 2.2m),
            (_, false, true) => (2.2m, 2m, 2.3m),
            _ => (2.2m, 2m, 2.5m),
        };
        return new SizingAdvice(SizingKind.Open, RaiseSize.Of(best), min, max, best);
    }

    /// <summary>The 3-bet size facing a single open to <paramref name="openToBigBlinds"/>, at an effective stack.</summary>
    public static SizingAdvice ThreeBet(PokerPosition defender, PokerPosition opener, decimal openToBigBlinds, decimal effectiveStackBigBlinds)
    {
        var shove = new SizingAdvice(SizingKind.ThreeBet, RaiseSize.AllIn, null, null, null);
        if (effectiveStackBigBlinds < ShoveThreeBetBelow)
        {
            return shove;
        }

        var (multiple, min, max) = InPosition(defender, opener) ? (3m, 2.5m, 3.3m) : (4m, 3.5m, 4.5m);
        var raiseTo = Math.Round(openToBigBlinds * multiple, 1, MidpointRounding.AwayFromZero);
        return raiseTo > effectiveStackBigBlinds * CommitShare
            ? shove
            : new SizingAdvice(SizingKind.ThreeBet, RaiseSize.Of(multiple), min, max, raiseTo);
    }

    /// <summary>
    /// Whether the 3-bettor acts after the opener on later streets: every seat but the blinds does; the big blind
    /// only against the small blind.
    /// </summary>
    public static bool InPosition(PokerPosition defender, PokerPosition opener) => defender switch
    {
        PokerPosition.SmallBlind => false,
        PokerPosition.BigBlind => opener == PokerPosition.SmallBlind,
        _ => true,
    };
}
