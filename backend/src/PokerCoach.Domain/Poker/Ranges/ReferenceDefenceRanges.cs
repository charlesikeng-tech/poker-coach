using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Domain.Poker.Ranges;

/// <summary>What a reference does with a hand facing an open.</summary>
public enum DefenceAction
{
    Fold,
    Call,
    ThreeBet,
}

/// <summary>A reference defence: the hands called and the hands re-raised, the rest folded.</summary>
public sealed record DefenceReference(
    IReadOnlySet<HandClass> Call,
    IReadOnlySet<HandClass> ThreeBet,
    string CallNotation,
    string ThreeBetNotation)
{
    public DefenceAction ActionFor(HandClass hand) =>
        ThreeBet.Contains(hand) ? DefenceAction.ThreeBet : Call.Contains(hand) ? DefenceAction.Call : DefenceAction.Fold;
}

/// <summary>
/// Reference defence ranges facing a single open, version 1 (ADR-0018): what a solid regular calls and 3-bets in
/// low-stakes online MTTs with antes, against an open of about 2–2.3 BB (3 BB from the small blind), chip EV, no
/// ICM. Our own conventions written as plain in/out grids, like the opening references (ADR-0009): references,
/// never "GTO". 3-bet bluffs are kept to suited wheel aces so the grids stay readable.
/// <para>
/// Written for 25 BB and more (mid and deep bands): below, 3-bets become shoves and flats shrink, a different
/// strategy not written yet. Openers are grouped (early: UTG–UTG+2, middle: lojack–hijack, then cutoff, button,
/// small blind) and seats go by distance to the button, so a 6-max UTG opens and defends like a full-ring lojack.
/// Only the spots that matter most have a reference: the big blind and the small blind against every opener, the
/// button and the cutoff against earlier seats, a middle seat against an early or middle open.
/// </para>
/// </summary>
public static class ReferenceDefenceRanges
{
    public const int Version = 1;

    private enum Defender
    {
        Middle,
        Cutoff,
        Button,
        SmallBlind,
        BigBlind,
    }

    private enum Opener
    {
        Early,
        Middle,
        Cutoff,
        Button,
        SmallBlind,
    }

    private static readonly Dictionary<(Defender, Opener), (string Call, string ThreeBet)> Notation = new()
    {
        [(Defender.BigBlind, Opener.Early)] = (
            "22-JJ, A2s-A3s, A6s-AQs, K9s+, Q9s+, J9s+, T8s+, 97s+, 86s+, 76s, 65s, 54s, ATo-AQo, KJo+, QJo",
            "QQ+, AKs, AKo, A5s-A4s"),
        [(Defender.BigBlind, Opener.Middle)] = (
            "22-TT, A2s-A3s, A6s-AJs, K7s+, Q8s+, J8s+, T7s+, 96s+, 86s+, 75s+, 64s+, 54s, A9o-AQo, KTo+, QTo+, JTo",
            "JJ+, AQs+, AKo, A5s-A4s"),
        [(Defender.BigBlind, Opener.Cutoff)] = (
            "22-99, A2s, A6s-ATs, K2s-KJs, Q5s-QJs, J6s-JTs, T6s-T9s, 96s-98s, 85s-87s, 74s-76s, 64s-65s, 53s-54s, 43s, A2o-AJo, K8o-KQo, Q9o-QJo, J9o-JTo, T9o, 98o",
            "TT+, AJs+, KQs, AQo+, A5s-A3s"),
        [(Defender.BigBlind, Opener.Button)] = (
            "22-88, A2s, A6s-A9s, K2s-KTs, Q2s-QJs, J4s-JTs, T5s-T9s, 95s-98s, 84s-87s, 74s-76s, 63s-65s, 53s-54s, 43s, A2o-ATo, K5o-KJo, Q7o-QJo, J8o-JTo, T8o-T9o, 97o-98o, 87o",
            "99+, ATs+, KJs+, AJo+, KQo, A5s-A3s"),
        [(Defender.BigBlind, Opener.SmallBlind)] = (
            "22-77, A6s-A8s, K2s-K9s, Q2s-QTs, J3s-JTs, T5s-T9s, 95s-98s, 84s-87s, 73s-76s, 63s-65s, 52s-54s, 42s-43s, 32s, A2o-A9o, K5o-KTo, Q7o-QJo, J7o-JTo, T7o-T9o, 97o-98o, 86o-87o, 76o",
            "88+, A9s+, KTs+, QJs, ATo+, KJo+, A5s-A2s"),

        [(Defender.SmallBlind, Opener.Early)] = ("99-JJ, AJs-AQs, KQs", "QQ+, AKs, AKo, A5s"),
        [(Defender.SmallBlind, Opener.Middle)] = ("88-TT, ATs-AJs, KJs, QJs, JTs", "JJ+, AQs+, AKo, KQs, A5s-A4s"),
        [(Defender.SmallBlind, Opener.Cutoff)] = ("66-99, A8s-A9s, KTs, QTs, JTs, T9s, 98s", "TT+, ATs+, KJs+, QJs, AJo+, KQo, A5s-A3s"),
        [(Defender.SmallBlind, Opener.Button)] = ("22-77, A6s-A7s, K8s, Q9s, J9s, T9s, 98s, 87s", "88+, A8s+, K9s+, QTs+, JTs, ATo+, KJo+, A5s-A2s"),

        [(Defender.Button, Opener.Early)] = ("22-JJ, ATs-AQs, KTs+, QTs+, JTs, T9s, 98s, 87s, AQo", "QQ+, AKs, AKo, A5s"),
        [(Defender.Button, Opener.Middle)] = ("22-TT, A9s-AJs, KTs+, QTs+, J9s+, T9s, 98s, 87s, 76s, AJo-AQo, KQo", "JJ+, AQs+, AKo, A5s-A4s"),
        [(Defender.Button, Opener.Cutoff)] = ("22-99, A7s-ATs, KTs-KJs, QTs+, J9s+, T8s+, 98s, 87s, 76s, 65s, ATo-AJo, KJo+", "TT+, AJs+, KQs, AQo+, A5s-A3s"),

        [(Defender.Cutoff, Opener.Early)] = ("77-JJ, AJs-AQs, KQs, QJs, JTs", "QQ+, AKs, AKo"),
        [(Defender.Cutoff, Opener.Middle)] = ("55-TT, ATs-AJs, KJs+, QJs, JTs, T9s, AQo", "JJ+, AQs+, AKo, A5s"),

        [(Defender.Middle, Opener.Early)] = ("88-JJ, AJs-AQs, KQs", "QQ+, AKs, AKo"),
        [(Defender.Middle, Opener.Middle)] = ("77-JJ, AJs-AQs, KQs, QJs, JTs", "QQ+, AKs, AKo, A5s"),
    };

    private static readonly Dictionary<(Defender, Opener), DefenceReference> Parsed = Notation.ToDictionary(
        e => e.Key,
        e => new DefenceReference(RangeNotation.Parse(e.Value.Call), RangeNotation.Parse(e.Value.ThreeBet), e.Value.Call, e.Value.ThreeBet));

    /// <summary>The stack bands a reference is written for: 25 BB and more.</summary>
    public static bool Covers(StackBand band) => band is StackBand.Mid or StackBand.Deep;

    /// <summary>
    /// The reference for a defender facing an open, or null when none is written (band below 25 BB, a spot left
    /// out, or a defender acting before the opener). Seats as <see cref="OpeningSeat.Canonical"/> names them.
    /// </summary>
    public static DefenceReference? For(StackBand band, TableFormat format, PokerPosition defender, PokerPosition opener) =>
        Covers(band) && DefenderOf(OpeningSeat.FullRingEquivalent(defender, format)) is { } d
            && OpenerOf(OpeningSeat.FullRingEquivalent(opener, format)) is { } o
            && Parsed.TryGetValue((d, o), out var reference)
            ? reference
            : null;

    /// <summary>Every written reference, for checks: no hand both called and 3-bet, every notation readable.</summary>
    public static IEnumerable<DefenceReference> All => Parsed.Values;

    private static Defender? DefenderOf(PokerPosition seat) => seat switch
    {
        PokerPosition.BigBlind => Defender.BigBlind,
        PokerPosition.SmallBlind => Defender.SmallBlind,
        PokerPosition.Button => Defender.Button,
        PokerPosition.Cutoff => Defender.Cutoff,
        PokerPosition.Hijack or PokerPosition.Lojack => Defender.Middle,
        _ => null,
    };

    private static Opener? OpenerOf(PokerPosition seat) => seat switch
    {
        PokerPosition.Utg or PokerPosition.Utg1 or PokerPosition.Utg2 => Opener.Early,
        PokerPosition.Lojack or PokerPosition.Hijack => Opener.Middle,
        PokerPosition.Cutoff => Opener.Cutoff,
        PokerPosition.Button => Opener.Button,
        PokerPosition.SmallBlind => Opener.SmallBlind,
        _ => null,
    };
}
