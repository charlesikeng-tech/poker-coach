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
}

/// <summary>
/// Reference opening ranges, version 1 (ADR-0009): what a solid regular raises first in, by position and
/// stack band, in low-stakes online MTTs with antes. Our own conventions, written as plain in/out grids
/// (real strategies mix some hands): references, never "GTO". Below 15 BB play is push/fold, covered
/// separately. Written for full-ring seats by distance to the button; 6-max seats map onto them
/// (<see cref="OpeningSeat.FullRingEquivalent"/>). Open rates stay inside the ADR-0007 reference rates.
/// </summary>
public static class ReferenceOpeningRanges
{
    public const int Version = 1;

    private static readonly Dictionary<(StackBand Band, PokerPosition Position), string> Notation = new()
    {
        [(StackBand.Deep, PokerPosition.Utg)] = "55+, A9s+, A5s-A4s, KTs+, QTs+, JTs, T9s, AJo+, KQo",
        [(StackBand.Deep, PokerPosition.Utg1)] = "44+, A8s+, A5s-A3s, KTs+, QTs+, J9s+, T9s, 98s, ATo+, KJo+",
        [(StackBand.Deep, PokerPosition.Utg2)] = "33+, A7s+, A5s-A2s, K9s+, Q9s+, J9s+, T9s, 98s, 87s, ATo+, KJo+, QJo",
        [(StackBand.Deep, PokerPosition.Lojack)] = "22+, A2s+, K8s+, Q9s+, J9s+, T8s+, 98s, 87s, 76s, A9o+, KTo+, QJo",
        [(StackBand.Deep, PokerPosition.Hijack)] = "22+, A2s+, K6s+, Q8s+, J8s+, T8s+, 97s+, 87s, 76s, 65s, A8o+, KTo+, QTo+, JTo",
        [(StackBand.Deep, PokerPosition.Cutoff)] = "22+, A2s+, K3s+, Q6s+, J7s+, T7s+, 96s+, 86s+, 75s+, 65s, 54s, A5o+, K9o+, Q9o+, J9o+",
        [(StackBand.Deep, PokerPosition.Button)] = "22+, A2s+, K2s+, Q2s+, J4s+, T6s+, 96s+, 85s+, 74s+, 64s+, 53s+, 43s, A2o+, K7o+, Q8o+, J8o+, T8o+, 98o, 87o",
        [(StackBand.Deep, PokerPosition.SmallBlind)] = "22+, A2s+, K2s+, Q4s+, J6s+, T6s+, 96s+, 85s+, 75s+, 64s+, 54s, A2o+, K8o+, Q9o+, J9o+, T9o",

        [(StackBand.Mid, PokerPosition.Utg)] = "66+, A9s+, A5s, KTs+, QTs+, JTs, AJo+, KQo",
        [(StackBand.Mid, PokerPosition.Utg1)] = "55+, A8s+, A5s-A4s, KTs+, QTs+, JTs, T9s, ATo+, KQo",
        [(StackBand.Mid, PokerPosition.Utg2)] = "44+, A7s+, A5s-A3s, K9s+, Q9s+, J9s+, T9s, 98s, ATo+, KJo+",
        [(StackBand.Mid, PokerPosition.Lojack)] = "33+, A2s+, K8s+, Q9s+, J9s+, T9s, 98s, 87s, A9o+, KJo+, QJo",
        [(StackBand.Mid, PokerPosition.Hijack)] = "22+, A2s+, K7s+, Q8s+, J8s+, T8s+, 98s, 87s, 76s, A8o+, KTo+, QJo",
        [(StackBand.Mid, PokerPosition.Cutoff)] = "22+, A2s+, K4s+, Q7s+, J7s+, T7s+, 97s+, 86s+, 76s, 65s, A5o+, K9o+, QTo+, JTo",
        [(StackBand.Mid, PokerPosition.Button)] = "22+, A2s+, K2s+, Q3s+, J5s+, T6s+, 96s+, 85s+, 75s+, 64s+, 54s, A2o+, K8o+, Q9o+, J9o+, T9o",
        [(StackBand.Mid, PokerPosition.SmallBlind)] = "22+, A2s+, K3s+, Q5s+, J7s+, T7s+, 97s+, 86s+, 76s, 65s, A2o+, K9o+, QTo+, JTo",

        [(StackBand.Short, PokerPosition.Utg)] = "55+, A9s+, A5s, KTs+, QJs, AJo+, KQo",
        [(StackBand.Short, PokerPosition.Utg1)] = "55+, A9s+, A5s, KTs+, QTs+, JTs, ATo+, KQo",
        [(StackBand.Short, PokerPosition.Utg2)] = "44+, A8s+, A5s-A4s, K9s+, QTs+, JTs, ATo+, KJo+",
        [(StackBand.Short, PokerPosition.Lojack)] = "33+, A7s+, A5s-A2s, K9s+, Q9s+, J9s+, T9s, A9o+, KJo+, QJo",
        [(StackBand.Short, PokerPosition.Hijack)] = "22+, A2s+, K8s+, Q9s+, J9s+, T9s, 98s, A8o+, KTo+, QJo",
        [(StackBand.Short, PokerPosition.Cutoff)] = "22+, A2s+, K6s+, Q8s+, J8s+, T8s+, 98s, 87s, A5o+, K9o+, QTo+, JTo",
        [(StackBand.Short, PokerPosition.Button)] = "22+, A2s+, K2s+, Q5s+, J7s+, T7s+, 97s+, 86s+, 76s, 65s, A2o+, K8o+, Q9o+, J9o+, T9o",
        [(StackBand.Short, PokerPosition.SmallBlind)] = "22+, A2s+, K5s+, Q7s+, J8s+, T8s+, 98s, 87s, A2o+, K9o+, QTo+, JTo",
    };

    private static readonly Dictionary<(StackBand, PokerPosition), IReadOnlySet<HandClass>> Parsed =
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

    /// <summary>
    /// The range, or null for a position that does not open. Ranges are written by distance to the button,
    /// so a 6-max UTG gets the full-ring lojack's.
    /// </summary>
    public static IReadOnlySet<HandClass>? For(StackBand band, TableFormat format, PokerPosition position) =>
        Parsed.GetValueOrDefault((band, OpeningSeat.FullRingEquivalent(position, format)));

    /// <summary>The range's notation, as written (shown to the player).</summary>
    public static string? NotationFor(StackBand band, TableFormat format, PokerPosition position) =>
        Notation.GetValueOrDefault((band, OpeningSeat.FullRingEquivalent(position, format)));

    /// <summary>Inclusive lower and exclusive upper bound in big blinds; no upper bound for deep.</summary>
    public static (decimal Min, decimal? Max) Bounds(StackBand band) => band switch
    {
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
