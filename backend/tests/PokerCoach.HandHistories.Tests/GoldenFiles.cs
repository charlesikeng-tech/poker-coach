namespace PokerCoach.HandHistories.Tests;

/// <summary>
/// Real Winamax files, anonymized: the account owner is "Hero", opponents are "VillainNN" variants that
/// keep the shape of the original names (spaces, dots, dashes).
/// </summary>
internal static class GoldenFiles
{
    /// <summary>10 € PKO, 6-max, hero 108th / 994. Partial coverage: levels 12–22, one table, 72 hands.</summary>
    public const string CassiopeiaHands = "20260905_CASSIOPEIA_1161415333__real_holdem_no-limit.txt";

    public const string CassiopeiaSummary = "20260905_CASSIOPEIA_1161415333__real_holdem_no-limit_summary.txt";

    /// <summary>5 € PKO, 6-max, hero 11th / 753 with bounties won. Level 1 to bust, 6 tables, 222 hands.</summary>
    public const string AcceleratorHands = "20260916_ACCELERATOR_1169257027__real_holdem_no-limit.txt";

    public const string AcceleratorSummary = "20260916_ACCELERATOR_1169257027__real_holdem_no-limit_summary.txt";

    /// <summary>
    /// 2 € + 2.50 € PKO, late registration, hero 2686th / 3808: out of the money, no bounty, so no
    /// "You won" line. Ends with a stray "\r" line as received.
    /// </summary>
    public const string QuantumSummary = "20261008_QUANTUM_1181101290__real_holdem_no-limit_summary.txt";

    /// <summary>10 € PKO, hero 185th / 550: out of the money with one bounty ("You won Bounty 1€").</summary>
    public const string ArcturusSummary = "20261002_ARCTURUS_1177277971__real_holdem_no-limit_summary.txt";

    /// <summary>
    /// 5 € PKO with a re-entry: two complete blocks in one file (1151st after 50 min, then 978th),
    /// both late registration. Registered players and prize pool grow from the first block to the second.
    /// </summary>
    public const string AsteroidSummary = "20261003_ASTEROID_1178140542__real_holdem_no-limit_summary.txt";

    public static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", "Winamax", fileName));

    /// <summary>Replaces the first occurrence only, and fails loudly if the text is not there.</summary>
    public static string ReplaceFirst(string content, string oldValue, string newValue)
    {
        var index = content.IndexOf(oldValue, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException($"Golden file does not contain: {oldValue}");
        }

        return content[..index] + newValue + content[(index + oldValue.Length)..];
    }
}
