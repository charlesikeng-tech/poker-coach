using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Domain.Poker.Ranges;

/// <summary>Effective stack bands the reference ranges are written for. Values travel in the API.</summary>
public enum StackBand
{
    /// <summary>15 to 25 big blinds: opens tighten, speculative hands drop out.</summary>
    Short,

    /// <summary>25 to 40 big blinds.</summary>
    Mid,

    /// <summary>40 big blinds and more.</summary>
    Deep,

    /// <summary>Below 15 big blinds: shove or fold, from the computed equilibrium (<see cref="PushFoldNash"/>).</summary>
    Push,
}

/// <summary>
/// Reference opening ranges, version 1 (ADR-0009): what a solid regular raises first in, by position,
/// stack band and stakes level, in online MTTs with antes. Deep (40+ BB) and mid (25–40 BB) ranges are
/// derived from a GTO preflop solver (pokerai.bet, 6-max with antes): hands whose raise frequency
/// exceeds 50% are "in". Short (15–25 BB) ranges are hand-written conventions. Below 15 BB play is
/// push/fold, covered separately. Written for full-ring seats by distance to the button; 6-max seats
/// map onto them (<see cref="OpeningSeat.FullRingEquivalent"/>). Open rates stay inside the ADR-0007
/// reference rates.
/// </summary>
public static class ReferenceOpeningRanges
{
    public const int Version = 1;

    private static readonly Dictionary<(StakesLevel Stakes, StackBand Band, PokerPosition Position), string> Notation = new()
    {
        // ── Low Stakes · Deep (40+ BB) ── GTO 6-max (pokerai.bet) ───────────────
        [(StakesLevel.Low, StackBand.Deep, PokerPosition.Utg)] = "66+, A3s+, K8s+, K6s, Q9s+, J9s+, T9s",
        [(StakesLevel.Low, StackBand.Deep, PokerPosition.Utg1)] = "66+, A3s+, K8s+, K6s, Q9s+, J9s+, T9s, AQo+",
        [(StakesLevel.Low, StackBand.Deep, PokerPosition.Utg2)] = "66+, A3s+, K8s+, K6s, Q9s+, J9s+, T9s, AJo+, KQo",
        [(StakesLevel.Low, StackBand.Deep, PokerPosition.Lojack)] = "66+, A3s+, K8s+, K6s, Q9s+, J9s+, T9s, ATo+, KJo+",
        [(StakesLevel.Low, StackBand.Deep, PokerPosition.Hijack)] = "44+, A2s+, K5s+, Q8s+, J8s+, T8s+, 98s, 87s, 76s, 54s, ATo+, KTo+, QJo",
        [(StakesLevel.Low, StackBand.Deep, PokerPosition.Cutoff)] = "22+, A2s+, K3s+, Q5s+, J7s+, T7s+, 97s+, 86s+, 76s, 65s, 54s, A8o+, KTo+, QTo+, JTo",
        [(StakesLevel.Low, StackBand.Deep, PokerPosition.Button)] = "22+, A2s+, K2s+, Q3s+, J4s+, T5s+, 96s+, 85s+, 75s+, 64s+, 53s+, A4o+, K8o+, Q9o+, J9o+, T8o+, 98o",
        [(StakesLevel.Low, StackBand.Deep, PokerPosition.SmallBlind)] = "22+, A2s+, K2s+, Q2s+, J5s+, T6s+, 96s+, 85s+, 75s+, 64s+, 53s+, A4o+, K8o+, Q9o+, J9o+, T8o+, 98o",

        // ── Low Stakes · Mid (25–40 BB) ── GTO 6-max 40bb (pokerai.bet) ────────
        [(StakesLevel.Low, StackBand.Mid, PokerPosition.Utg)] = "66+, A2s+, K7s+, Q9s+, J9s+, T9s, AKo",
        [(StakesLevel.Low, StackBand.Mid, PokerPosition.Utg1)] = "66+, A2s+, K7s+, Q9s+, J9s+, T9s, AJo+",
        [(StakesLevel.Low, StackBand.Mid, PokerPosition.Utg2)] = "66+, A2s+, K7s+, Q9s+, J9s+, T9s, ATo+, KQo",
        [(StakesLevel.Low, StackBand.Mid, PokerPosition.Lojack)] = "66+, A2s+, K7s+, Q9s+, J9s+, T9s, ATo+, KJo+, QJo",
        [(StakesLevel.Low, StackBand.Mid, PokerPosition.Hijack)] = "55+, A2s+, K5s+, Q8s+, J8s+, T8s+, 98s, A9o+, KTo+, QTo+, JTo",
        [(StakesLevel.Low, StackBand.Mid, PokerPosition.Cutoff)] = "44+, A2s+, K4s+, Q6s+, J7s+, T7s+, 97s+, 87s, 76s, A8o+, K9o+, QTo+, JTo",
        [(StakesLevel.Low, StackBand.Mid, PokerPosition.Button)] = "33+, A2s+, K2s+, Q4s+, J5s+, T6s+, 96s+, 86s+, 75s+, 65s, 54s, A4o+, K8o+, Q9o+, J9o+, T8o+, 98o",
        [(StakesLevel.Low, StackBand.Mid, PokerPosition.SmallBlind)] = "22+, A2s+, K2s+, Q2s+, J3s+, T5s+, 95s+, 85s+, 74s+, 64s+, 54s, A3o+, K7o+, Q8o+, J8o+, T8o+, 98o",

        // ── Low Stakes · Short (15–25 BB) ───────────────────────────────────────
        [(StakesLevel.Low, StackBand.Short, PokerPosition.Utg)] = "55+, A9s+, A5s, KTs+, QJs, AJo+, KQo",
        [(StakesLevel.Low, StackBand.Short, PokerPosition.Utg1)] = "55+, A9s+, A5s, KTs+, QTs+, JTs, ATo+, KQo",
        [(StakesLevel.Low, StackBand.Short, PokerPosition.Utg2)] = "44+, A8s+, A5s-A4s, K9s+, QTs+, JTs, ATo+, KJo+",
        [(StakesLevel.Low, StackBand.Short, PokerPosition.Lojack)] = "33+, A7s+, A5s-A2s, K9s+, Q9s+, J9s+, T9s, A9o+, KJo+, QJo",
        [(StakesLevel.Low, StackBand.Short, PokerPosition.Hijack)] = "22+, A2s+, K8s+, Q9s+, J9s+, T9s, 98s, A8o+, KTo+, QJo",
        [(StakesLevel.Low, StackBand.Short, PokerPosition.Cutoff)] = "22+, A2s+, K6s+, Q8s+, J8s+, T8s+, 98s, 87s, A5o+, K9o+, QTo+, JTo",
        [(StakesLevel.Low, StackBand.Short, PokerPosition.Button)] = "22+, A2s+, K2s+, Q5s+, J7s+, T7s+, 97s+, 86s+, 76s, 65s, A2o+, K8o+, Q9o+, J9o+, T9o",
        [(StakesLevel.Low, StackBand.Short, PokerPosition.SmallBlind)] = "22+, A2s+, K5s+, Q7s+, J8s+, T8s+, 98s, 87s, A2o+, K9o+, QTo+, JTo",

        // ── Mid Stakes · Deep (40+ BB) ── GTO 6-max 100NL (pokerai.bet) ────────
        [(StakesLevel.Mid, StackBand.Deep, PokerPosition.Utg)] = "88+, A2s+, K5s+, Q9s+, JTs, AKo",
        [(StakesLevel.Mid, StackBand.Deep, PokerPosition.Utg1)] = "88+, A2s+, K5s+, Q9s+, JTs, AJo+",
        [(StakesLevel.Mid, StackBand.Deep, PokerPosition.Utg2)] = "88+, A2s+, K5s+, Q9s+, JTs, ATo+, KQo",
        [(StakesLevel.Mid, StackBand.Deep, PokerPosition.Lojack)] = "88+, A2s+, K5s+, Q9s+, JTs, ATo+, KJo+, QJo",
        [(StakesLevel.Mid, StackBand.Deep, PokerPosition.Hijack)] = "66+, A2s+, K5s+, Q9s+, J9s+, A9o+, KTo+, QTo+",
        [(StakesLevel.Mid, StackBand.Deep, PokerPosition.Cutoff)] = "55+, A2s+, K3s+, Q6s+, J8s+, T8s+, 98s, A8o+, A5o, KTo+, QTo+, JTo",
        [(StakesLevel.Mid, StackBand.Deep, PokerPosition.Button)] = "33+, A2s+, K2s+, Q3s+, J5s+, T6s+, 96s+, 86s+, 76s, 65s, 54s, A4o+, K9o+, Q9o+, J9o+, T8o+",
        [(StakesLevel.Mid, StackBand.Deep, PokerPosition.SmallBlind)] = "TT+, 33-22, A8s+, K6s-K5s, K3s-K2s, KTs+, Q7s-Q2s, QTs+, J7s-J5s, JTs, T7s-T6s, 97s-96s, 86s-85s, 75s+, 64s+, 53s, AJo+, A9o-A7o, A5o, K9o, QTo, J9o, T9o",

        // ── Mid Stakes · Mid (25–40 BB) ── GTO 6-max 40bb (pokerai.bet) ────────
        [(StakesLevel.Mid, StackBand.Mid, PokerPosition.Utg)] = "66+, A2s+, K7s+, Q9s+, J9s+, T9s, AKo",
        [(StakesLevel.Mid, StackBand.Mid, PokerPosition.Utg1)] = "66+, A2s+, K7s+, Q9s+, J9s+, T9s, AJo+",
        [(StakesLevel.Mid, StackBand.Mid, PokerPosition.Utg2)] = "66+, A2s+, K7s+, Q9s+, J9s+, T9s, ATo+, KQo",
        [(StakesLevel.Mid, StackBand.Mid, PokerPosition.Lojack)] = "66+, A2s+, K7s+, Q9s+, J9s+, T9s, ATo+, KJo+, QJo",
        [(StakesLevel.Mid, StackBand.Mid, PokerPosition.Hijack)] = "55+, A2s+, K5s+, Q8s+, J8s+, T8s+, 98s, A9o+, KTo+, QTo+, JTo",
        [(StakesLevel.Mid, StackBand.Mid, PokerPosition.Cutoff)] = "44+, A2s+, K4s+, Q6s+, J7s+, T7s+, 97s+, 87s, 76s, A8o+, K9o+, QTo+, JTo",
        [(StakesLevel.Mid, StackBand.Mid, PokerPosition.Button)] = "33+, A2s+, K2s+, Q4s+, J5s+, T6s+, 96s+, 86s+, 75s+, 65s, 54s, A4o+, K8o+, Q9o+, J9o+, T8o+, 98o",
        [(StakesLevel.Mid, StackBand.Mid, PokerPosition.SmallBlind)] = "22+, A2s+, K2s+, Q2s+, J3s+, T5s+, 95s+, 85s+, 74s+, 64s+, 54s, A3o+, K7o+, Q8o+, J8o+, T8o+, 98o",

        // ── Mid Stakes · Short (15–25 BB) ───────────────────────────────────────
        [(StakesLevel.Mid, StackBand.Short, PokerPosition.Utg)] = "66+, ATs+, A5s, KTs+, QJs, AJo+, KQo",
        [(StakesLevel.Mid, StackBand.Short, PokerPosition.Utg1)] = "55+, A9s+, A5s, KTs+, QTs+, JTs, ATo+, KQo",
        [(StakesLevel.Mid, StackBand.Short, PokerPosition.Utg2)] = "44+, A8s+, A5s-A4s, K9s+, QTs+, JTs, ATo+, KJo+",
        [(StakesLevel.Mid, StackBand.Short, PokerPosition.Lojack)] = "33+, A7s+, A5s-A2s, K9s+, Q9s+, J9s+, T9s, A9o+, KJo+, QJo",
        [(StakesLevel.Mid, StackBand.Short, PokerPosition.Hijack)] = "22+, A2s+, K8s+, Q9s+, J9s+, T9s, 98s, A9o+, KTo+, QJo",
        [(StakesLevel.Mid, StackBand.Short, PokerPosition.Cutoff)] = "22+, A2s+, K7s+, Q9s+, J8s+, T8s+, 98s, 87s, A5o+, K9o+, QTo+, JTo",
        [(StakesLevel.Mid, StackBand.Short, PokerPosition.Button)] = "22+, A2s+, K3s+, Q6s+, J7s+, T7s+, 97s+, 86s+, 76s, 65s, A2o+, K9o+, QTo+, JTo",
        [(StakesLevel.Mid, StackBand.Short, PokerPosition.SmallBlind)] = "22+, A2s+, K5s+, Q7s+, J8s+, T8s+, 98s, 87s, A2o+, K9o+, QTo+, JTo",

        // ── High Stakes · Deep (40+ BB) ── GTO 6-max 200NL (pokerai.bet) ───────
        [(StakesLevel.High, StackBand.Deep, PokerPosition.Utg)] = "66+, A2s+, K5s+, Q9s+, JTs, AQo+",
        [(StakesLevel.High, StackBand.Deep, PokerPosition.Utg1)] = "66+, A2s+, K5s+, Q9s+, JTs, AJo+",
        [(StakesLevel.High, StackBand.Deep, PokerPosition.Utg2)] = "66+, A2s+, K5s+, Q9s+, JTs, ATo+, KQo",
        [(StakesLevel.High, StackBand.Deep, PokerPosition.Lojack)] = "66+, A2s+, K5s+, Q9s+, JTs, ATo+, KTo+, QJo",
        [(StakesLevel.High, StackBand.Deep, PokerPosition.Hijack)] = "55+, A2s+, K4s+, Q8s+, J9s+, T9s, A9o+, KTo+, QTo+, JTo",
        [(StakesLevel.High, StackBand.Deep, PokerPosition.Cutoff)] = "44+, A2s+, K3s+, Q5s+, J7s+, T7s+, 97s+, 87s, A8o+, A5o, K9o+, QTo+, JTo",
        [(StakesLevel.High, StackBand.Deep, PokerPosition.Button)] = "22+, A2s+, K2s+, Q2s+, J4s+, T6s+, 96s+, 86s+, 75s+, 65s, 54s, A3o+, K8o+, Q9o+, J8o+, T8o+",
        [(StakesLevel.High, StackBand.Deep, PokerPosition.SmallBlind)] = "22+, A2s+, K2s+, Q2s+, J4s+, T6s+, 96s+, 85s+, 75s+, 64s+, 54s, A4o+, K8o+, Q9o+, J9o+, T8o+, 98o",

        // ── High Stakes · Mid (25–40 BB) ── GTO 6-max 40bb (pokerai.bet) ───────
        [(StakesLevel.High, StackBand.Mid, PokerPosition.Utg)] = "66+, A2s+, K7s+, Q9s+, J9s+, T9s, AKo",
        [(StakesLevel.High, StackBand.Mid, PokerPosition.Utg1)] = "66+, A2s+, K7s+, Q9s+, J9s+, T9s, AJo+",
        [(StakesLevel.High, StackBand.Mid, PokerPosition.Utg2)] = "66+, A2s+, K7s+, Q9s+, J9s+, T9s, ATo+, KQo",
        [(StakesLevel.High, StackBand.Mid, PokerPosition.Lojack)] = "66+, A2s+, K7s+, Q9s+, J9s+, T9s, ATo+, KJo+, QJo",
        [(StakesLevel.High, StackBand.Mid, PokerPosition.Hijack)] = "55+, A2s+, K5s+, Q8s+, J8s+, T8s+, 98s, A9o+, KTo+, QTo+, JTo",
        [(StakesLevel.High, StackBand.Mid, PokerPosition.Cutoff)] = "44+, A2s+, K4s+, Q6s+, J7s+, T7s+, 97s+, 87s, 76s, A8o+, K9o+, QTo+, JTo",
        [(StakesLevel.High, StackBand.Mid, PokerPosition.Button)] = "33+, A2s+, K2s+, Q4s+, J5s+, T6s+, 96s+, 86s+, 75s+, 65s, 54s, A4o+, K8o+, Q9o+, J9o+, T8o+, 98o",
        [(StakesLevel.High, StackBand.Mid, PokerPosition.SmallBlind)] = "22+, A2s+, K2s+, Q2s+, J3s+, T5s+, 95s+, 85s+, 74s+, 64s+, 54s, A3o+, K7o+, Q8o+, J8o+, T8o+, 98o",

        // ── High Stakes · Short (15–25 BB) ──────────────────────────────────────
        [(StakesLevel.High, StackBand.Short, PokerPosition.Utg)] = "77+, ATs+, A5s, KTs+, QJs, AJo+, KQo",
        [(StakesLevel.High, StackBand.Short, PokerPosition.Utg1)] = "66+, ATs+, A5s, KTs+, QTs+, JTs, AJo+, KQo",
        [(StakesLevel.High, StackBand.Short, PokerPosition.Utg2)] = "55+, A8s+, A5s-A4s, KTs+, QTs+, JTs, ATo+, KJo+",
        [(StakesLevel.High, StackBand.Short, PokerPosition.Lojack)] = "44+, A5s+, K9s+, QTs+, J9s+, T9s, A9o+, KJo+, QJo",
        [(StakesLevel.High, StackBand.Short, PokerPosition.Hijack)] = "33+, A2s+, K8s+, Q9s+, J9s+, T9s, 98s, A9o+, KTo+, QJo",
        [(StakesLevel.High, StackBand.Short, PokerPosition.Cutoff)] = "22+, A2s+, K7s+, Q9s+, J8s+, T8s+, 98s, 87s, A7o+, K9o+, QTo+, JTo",
        [(StakesLevel.High, StackBand.Short, PokerPosition.Button)] = "22+, A2s+, K3s+, Q6s+, J7s+, T7s+, 97s+, 86s+, 76s, 65s, A3o+, K9o+, QTo+, JTo",
        [(StakesLevel.High, StackBand.Short, PokerPosition.SmallBlind)] = "22+, A2s+, K6s+, Q8s+, J8s+, T8s+, 98s, 87s, A2o+, K9o+, QTo+, JTo",
    };

    private static readonly Dictionary<(StakesLevel, StackBand, PokerPosition), IReadOnlySet<HandClass>> Parsed =
        Notation.ToDictionary(e => e.Key, e => RangeNotation.Parse(e.Value));

    private static readonly PokerPosition[] FullRingPositions =
    [
        PokerPosition.Utg,
        PokerPosition.Utg1,
        PokerPosition.Utg2,
        PokerPosition.Lojack,
        PokerPosition.Hijack,
        PokerPosition.Cutoff,
        PokerPosition.Button,
        PokerPosition.SmallBlind,
    ];

    private static readonly PokerPosition[] SixMaxPositions =
    [
        PokerPosition.Utg,
        PokerPosition.Hijack,
        PokerPosition.Cutoff,
        PokerPosition.Button,
        PokerPosition.SmallBlind,
    ];

    /// <summary>Positions that open at this table format, first to act first (the big blind never opens).</summary>
    public static IReadOnlyList<PokerPosition> Positions(TableFormat format) =>
        format == TableFormat.SixMax ? SixMaxPositions : FullRingPositions;

    /// <summary>Stack used for the push/fold reference when none is given.</summary>
    public const int DefaultPushStack = 10;

    /// <summary>
    /// The range, or null for a position that does not open. Written ranges go by distance to the button,
    /// so a 6-max UTG gets the full-ring lojack's; push/fold ranges are computed for the format itself, at
    /// <paramref name="pushStack"/> big blinds.
    /// </summary>
    public static IReadOnlySet<HandClass>? For(StackBand band, TableFormat format, PokerPosition position, int? pushStack = null, StakesLevel stakesLevel = default)
    {
        if (band == StackBand.Push)
        {
            return Positions(format).Contains(position)
                ? PushFoldNash.Solve(format, pushStack ?? DefaultPushStack).Shove(position)
                : null;
        }

        return Parsed.GetValueOrDefault((stakesLevel, band, OpeningSeat.FullRingEquivalent(position, format)));
    }

    /// <summary>The range's notation, as written (shown to the player); null for computed push/fold ranges.</summary>
    public static string? NotationFor(StackBand band, TableFormat format, PokerPosition position, StakesLevel stakesLevel = default) =>
        band == StackBand.Push ? null : Notation.GetValueOrDefault((stakesLevel, band, OpeningSeat.FullRingEquivalent(position, format)));

    /// <summary>Inclusive lower and exclusive upper bound in big blinds; no upper bound for deep.</summary>
    public static (decimal Min, decimal? Max) Bounds(StackBand band) => band switch
    {
        StackBand.Push => (0m, 15m),
        StackBand.Short => (15m, 25m),
        StackBand.Mid => (25m, 40m),
        _ => (40m, null),
    };

    /// <summary>Share of the 1,326 two-card holdings the range opens.</summary>
    public static decimal ComboShare(IReadOnlySet<HandClass> range)
    {
        ArgumentNullException.ThrowIfNull(range);
        return Math.Round(range.Sum(h => h.Combinations) / 1326m, 4);
    }
}
