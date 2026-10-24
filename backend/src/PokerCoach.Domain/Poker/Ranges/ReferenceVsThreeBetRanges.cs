using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Domain.Poker.Ranges;

/// <summary>What a reference does facing a 3-bet after opening.</summary>
public enum VsThreeBetAction
{
    Fold,
    Call,
    FourBet,
}

/// <summary>A reference vs-3-bet range: the hands called and the hands 4-bet, the rest folded.</summary>
public sealed record VsThreeBetReference(
    IReadOnlySet<HandClass> Call,
    IReadOnlySet<HandClass> FourBet,
    string CallNotation,
    string FourBetNotation)
{
    public VsThreeBetAction ActionFor(HandClass hand) =>
        FourBet.Contains(hand) ? VsThreeBetAction.FourBet : Call.Contains(hand) ? VsThreeBetAction.Call : VsThreeBetAction.Fold;
}

/// <summary>
/// Reference vs-3-bet ranges, version 1: what a solid regular does when he opened and faces a single 3-bet
/// in low-stakes online MTTs with antes, chip EV, no ICM. Written for 25 BB and more (mid and deep bands).
/// <para>
/// Principles: from early positions the opening range is tight so vs a 3-bet the hero defends mostly with
/// premiums and suited broadways — the continue range is narrow. From later positions the opening range is
/// wider, so the hero continues wider (more calls with suited connectors, suited broadways) and adds 4-bet
/// bluffs (suited wheel aces). IP (opener is in position vs the 3-bettor) widens the calling range.
/// OOP (opener is out of position) shrinks calls and shifts toward 4-betting.
/// </para>
/// <para>
/// 4-bet value: AA, KK always. QQ 4-bets vs late-position 3-bets; AKs 4-bets from most spots.
/// 4-bet bluffs: A5s–A2s from steal positions facing a resteal; kept minimal from early positions.
/// Calling: JJ–TT, AQs, AJs, KQs and suited connectors (IP) or mostly fold (OOP from EP).
/// </para>
/// </summary>
public static class ReferenceVsThreeBetRanges
{
    public const int Version = 1;

    private enum Opener
    {
        Early,
        Middle,
        Cutoff,
        Button,
        SmallBlind,
    }

    private enum ThreeBettor
    {
        Middle,
        Cutoff,
        Button,
        SmallBlind,
        BigBlind,
    }

    private static readonly Dictionary<(Opener, ThreeBettor), (string Call, string FourBet)> Notation = new()
    {
        // Early open (UTG–UTG+2) facing a 3-bet: tight continue range, mostly premiums.
        [(Opener.Early, ThreeBettor.Middle)] = (
            "QQ-JJ, AKo",
            "AA, KK, AKs"),
        [(Opener.Early, ThreeBettor.Cutoff)] = (
            "QQ-JJ, AKo",
            "AA, KK, AKs"),
        [(Opener.Early, ThreeBettor.Button)] = (
            "QQ-JJ, AKo, AQs",
            "AA, KK, AKs"),
        [(Opener.Early, ThreeBettor.SmallBlind)] = (
            "QQ-JJ, AKo, AQs",
            "AA, KK, AKs"),
        [(Opener.Early, ThreeBettor.BigBlind)] = (
            "QQ-JJ, AKo, AQs",
            "AA, KK, AKs"),

        // Middle open (LJ–HJ) facing a 3-bet: slightly wider continue.
        [(Opener.Middle, ThreeBettor.Cutoff)] = (
            "JJ-TT, AQs, AQo",
            "AA, KK, QQ, AKs, AKo"),
        [(Opener.Middle, ThreeBettor.Button)] = (
            "JJ-TT, AQs, AJs, KQs, AQo",
            "AA, KK, QQ, AKs, AKo, A5s"),
        [(Opener.Middle, ThreeBettor.SmallBlind)] = (
            "JJ-TT, AQs, AQo",
            "AA, KK, QQ, AKs, AKo"),
        [(Opener.Middle, ThreeBettor.BigBlind)] = (
            "JJ-TT, AQs, AJs, KQs, AQo",
            "AA, KK, QQ, AKs, AKo, A5s"),

        // Cutoff open facing a 3-bet: wider opening range, wider continue.
        [(Opener.Cutoff, ThreeBettor.Button)] = (
            "TT-99, AJs, ATs, KQs, KJs, QJs, JTs, AQo",
            "AA, KK, QQ, JJ, AKs, AQs, AKo, A5s-A4s"),
        [(Opener.Cutoff, ThreeBettor.SmallBlind)] = (
            "JJ-TT, AQs, AJs, KQs, AQo",
            "AA, KK, QQ, AKs, AKo, A5s"),
        [(Opener.Cutoff, ThreeBettor.BigBlind)] = (
            "TT-99, AJs, ATs, KQs, KJs, QJs, JTs, AQo",
            "AA, KK, QQ, JJ, AKs, AQs, AKo, A5s-A4s"),

        // Button open facing a 3-bet: widest continue because of positional advantage and wide open.
        [(Opener.Button, ThreeBettor.SmallBlind)] = (
            "TT-88, AJs-A9s, KQs-KTs, QJs-QTs, JTs, T9s, 98s, AJo-ATo, KQo",
            "AA, KK, QQ, JJ, AKs, AQs, AKo, A5s-A2s"),
        [(Opener.Button, ThreeBettor.BigBlind)] = (
            "TT-88, AJs-A9s, KQs-KTs, QJs-QTs, JTs, T9s, 98s, 87s, AJo-ATo, KQo",
            "AA, KK, QQ, JJ, AKs, AQs, AKo, A5s-A2s"),

        // Small blind open facing a BB 3-bet: OOP but wide open, moderate continue.
        [(Opener.SmallBlind, ThreeBettor.BigBlind)] = (
            "TT-88, AJs-A9s, KQs-KTs, QJs, JTs, T9s, AJo-ATo, KQo",
            "AA, KK, QQ, JJ, AKs, AQs, AKo, A5s-A3s"),
    };

    private static readonly Dictionary<(Opener, ThreeBettor), VsThreeBetReference> Parsed = Notation.ToDictionary(
        e => e.Key,
        e => new VsThreeBetReference(RangeNotation.Parse(e.Value.Call), RangeNotation.Parse(e.Value.FourBet), e.Value.Call, e.Value.FourBet));

    /// <summary>The stack bands a reference is written for: 25 BB and more.</summary>
    public static bool Covers(StackBand band) => band is StackBand.Mid or StackBand.Deep;

    /// <summary>
    /// The reference for an opener facing a 3-bet, or null when none is written (band below 25 BB, a spot
    /// left out, or an opener acting after the 3-bettor). Seats as <see cref="OpeningSeat.Canonical"/> names them.
    /// </summary>
    public static VsThreeBetReference? For(StackBand band, TableFormat format, PokerPosition opener, PokerPosition threeBettor) =>
        Covers(band) && OpenerOf(OpeningSeat.FullRingEquivalent(opener, format)) is { } o
            && ThreeBettorOf(OpeningSeat.FullRingEquivalent(threeBettor, format)) is { } tb
            && Parsed.TryGetValue((o, tb), out var reference)
            ? reference
            : null;

    /// <summary>Every written reference, for checks: no hand both called and 4-bet, every notation readable.</summary>
    public static IEnumerable<VsThreeBetReference> All => Parsed.Values;

    private static Opener? OpenerOf(PokerPosition seat) => seat switch
    {
        PokerPosition.Utg or PokerPosition.Utg1 or PokerPosition.Utg2 => Opener.Early,
        PokerPosition.Lojack or PokerPosition.Hijack => Opener.Middle,
        PokerPosition.Cutoff => Opener.Cutoff,
        PokerPosition.Button => Opener.Button,
        PokerPosition.SmallBlind => Opener.SmallBlind,
        _ => null,
    };

    private static ThreeBettor? ThreeBettorOf(PokerPosition seat) => seat switch
    {
        PokerPosition.Lojack or PokerPosition.Hijack => ThreeBettor.Middle,
        PokerPosition.Cutoff => ThreeBettor.Cutoff,
        PokerPosition.Button => ThreeBettor.Button,
        PokerPosition.SmallBlind => ThreeBettor.SmallBlind,
        PokerPosition.BigBlind => ThreeBettor.BigBlind,
        _ => null,
    };
}
