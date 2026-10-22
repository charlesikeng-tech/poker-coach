using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Domain.Poker.Ranges;

/// <summary>Packs a subset of the 169 hand classes into a compact 22-byte bitset (176 bits, first 169 used).</summary>
internal static class HandBitset
{
    public const int ByteLength = 22;

    public static byte[] FromIndices(IReadOnlySet<int> indices)
    {
        var data = new byte[ByteLength];
        foreach (var index in indices)
        {
            if (index is < 0 or > 168)
                throw new ArgumentOutOfRangeException(nameof(indices), index, "Index must be between 0 and 168.");
            data[index >> 3] |= (byte)(1 << (index & 7));
        }
        return data;
    }

    public static bool Get(byte[] data, int index) =>
        (data[index >> 3] & (1 << (index & 7))) != 0;

    public static HashSet<int> ToIndices(byte[] data)
    {
        var indices = new HashSet<int>();
        for (var i = 0; i < 169; i++)
        {
            if (Get(data, i))
                indices.Add(i);
        }
        return indices;
    }

    public static IReadOnlySet<HandClass> ToHandClasses(byte[] data)
    {
        var all = HandClass.All;
        var set = new HashSet<HandClass>();
        for (var i = 0; i < 169; i++)
        {
            if (Get(data, i))
                set.Add(all[i]);
        }
        return set;
    }
}

/// <summary>
/// A user's custom opening range for a specific (format, band, position) slot. Replaces the hard-coded
/// reference when present, so the gap analysis compares the player's stats against their own target.
/// </summary>
public sealed class CustomOpeningRange
{
    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public TableFormat Format { get; private set; }

    public StackBand Band { get; private set; }

    public PokerPosition Position { get; private set; }

    public byte[] Hands { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private CustomOpeningRange() { }

    public static CustomOpeningRange Create(
        Guid userId, TableFormat format, StackBand band, PokerPosition position,
        IReadOnlySet<int> indices, DateTimeOffset now)
    {
        return new CustomOpeningRange
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            Format = format,
            Band = band,
            Position = position,
            Hands = HandBitset.FromIndices(indices),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(IReadOnlySet<int> indices, DateTimeOffset now)
    {
        Hands = HandBitset.FromIndices(indices);
        UpdatedAt = now;
    }

    public bool Contains(int index) => HandBitset.Get(Hands, index);

    public IReadOnlySet<HandClass> ToHandClassSet() => HandBitset.ToHandClasses(Hands);
}

/// <summary>
/// A user's custom defence range for a specific (format, band, defender, opener) slot: which hands to call,
/// which to 3-bet, the rest folded. Replaces the hard-coded defence reference when present.
/// </summary>
public sealed class CustomDefenceRange
{
    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public TableFormat Format { get; private set; }

    public StackBand Band { get; private set; }

    public PokerPosition Defender { get; private set; }

    public PokerPosition Opener { get; private set; }

    public byte[] CallHands { get; private set; } = [];

    public byte[] ThreeBetHands { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private CustomDefenceRange() { }

    public static CustomDefenceRange Create(
        Guid userId, TableFormat format, StackBand band,
        PokerPosition defender, PokerPosition opener,
        IReadOnlySet<int> callIndices, IReadOnlySet<int> threeBetIndices,
        DateTimeOffset now)
    {
        ValidateNoOverlap(callIndices, threeBetIndices);
        return new CustomDefenceRange
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            Format = format,
            Band = band,
            Defender = defender,
            Opener = opener,
            CallHands = HandBitset.FromIndices(callIndices),
            ThreeBetHands = HandBitset.FromIndices(threeBetIndices),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(IReadOnlySet<int> callIndices, IReadOnlySet<int> threeBetIndices, DateTimeOffset now)
    {
        ValidateNoOverlap(callIndices, threeBetIndices);
        CallHands = HandBitset.FromIndices(callIndices);
        ThreeBetHands = HandBitset.FromIndices(threeBetIndices);
        UpdatedAt = now;
    }

    public DefenceAction? ActionFor(int index) =>
        HandBitset.Get(ThreeBetHands, index) ? DefenceAction.ThreeBet :
        HandBitset.Get(CallHands, index) ? DefenceAction.Call :
        null;

    public DefenceReference ToDefenceReference()
    {
        var callSet = HandBitset.ToHandClasses(CallHands);
        var threeBetSet = HandBitset.ToHandClasses(ThreeBetHands);
        return new DefenceReference(callSet, threeBetSet, CallNotation: "", ThreeBetNotation: "");
    }

    private static void ValidateNoOverlap(IReadOnlySet<int> callIndices, IReadOnlySet<int> threeBetIndices)
    {
        foreach (var index in callIndices)
        {
            if (threeBetIndices.Contains(index))
                throw new ArgumentException($"Hand index {index} cannot be in both call and 3-bet sets.");
        }
    }
}
