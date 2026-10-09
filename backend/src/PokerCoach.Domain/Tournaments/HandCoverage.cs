namespace PokerCoach.Domain.Tournaments;

/// <summary>One of the hero's hands in a tournament, reduced to what coverage needs.</summary>
/// <param name="TableKey">Table instance; null when the room's hand ids do not tell.</param>
/// <param name="HandNumber">Hand number on that table; null with <paramref name="TableKey"/>.</param>
/// <param name="HeroStack">Hero's chips at the start of the hand.</param>
/// <param name="NetChips">Hero's chips won minus put in during the hand.</param>
public sealed record HandPoint(DateTimeOffset StartedAt, int Level, string? TableKey, long? HandNumber, long HeroStack, long NetChips);

/// <summary>What the summary file says that coverage can check against; all null without a summary.</summary>
/// <param name="ChipPurchases">Rebuys and add-ons bought: chips that arrive between hands.</param>
public sealed record SummaryFacts(int? Entries, bool? FirstEntryLateRegistration, int? LastFinishPosition, int ChipPurchases = 0);

public enum CoverageStatus
{
    /// <summary>No hand imported for the tournament.</summary>
    NoHands,

    /// <summary>No missing hand, no unexplained stack change, the end is in the file, the start is not known to be missing.</summary>
    Complete,

    Partial,
}

/// <summary>How much of the hero's tournament the imported hands cover.</summary>
/// <param name="MissingHands">Hands absent from table sequences (numbers skipped between two hero hands on one table).</param>
/// <param name="StackBreaks">Consecutive hands where the hero's stack does not follow (hands missing elsewhere).</param>
/// <param name="EntriesSeen">Entries visible in the hands: a bust followed by a new stack starts one.</param>
/// <param name="StartMissing">
/// True when the player registered on time but the first hand is past level 1; null when it cannot be told
/// (late registration, or no summary).
/// </param>
/// <param name="EndSeen">The last hand ends the hero's tournament (stack 0) or the summary says he won it.</param>
public sealed record TournamentCoverage(
    CoverageStatus Status,
    int HandCount,
    int? FirstLevel,
    int? LastLevel,
    int MissingHands,
    int StackBreaks,
    int EntriesSeen,
    bool? StartMissing,
    bool EndSeen)
{
    /// <summary>Bump when a rule below changes: stored coverage is recomputed.</summary>
    public const int Version = 2;

    public static TournamentCoverage Analyze(IReadOnlyList<HandPoint> hands, SummaryFacts summary)
    {
        ArgumentNullException.ThrowIfNull(hands);
        ArgumentNullException.ThrowIfNull(summary);

        if (hands.Count == 0)
        {
            return new TournamentCoverage(CoverageStatus.NoHands, 0, null, null, 0, 0, 0, null, false);
        }

        var ordered = hands.OrderBy(h => h.StartedAt).ThenBy(h => h.HandNumber).ToList();
        var entries = 1;
        var stackBreaks = 0;
        var purchasesLeft = summary.ChipPurchases;
        for (var i = 1; i < ordered.Count; i++)
        {
            var previousEnd = ordered[i - 1].HeroStack + ordered[i - 1].NetChips;
            if (ordered[i].HeroStack == previousEnd)
            {
                continue;
            }

            if (ordered[i].HeroStack > previousEnd && purchasesLeft > 0)
            {
                // Chips that arrived between hands: a rebuy or an add-on the summary accounts for
                // (a rebuy after busting included: it is not a new entry).
                purchasesLeft--;
            }
            else if (previousEnd == 0)
            {
                // Busted, then back with a new stack: a re-entry.
                entries++;
            }
            else
            {
                stackBreaks++;
            }
        }

        var missing = ordered
            .Where(h => h.TableKey is not null && h.HandNumber is not null)
            .GroupBy(h => h.TableKey, StringComparer.Ordinal)
            .Sum(table =>
            {
                var numbers = table.Select(h => h.HandNumber!.Value).Distinct().Order().ToList();
                var gaps = 0L;
                for (var i = 1; i < numbers.Count; i++)
                {
                    gaps += numbers[i] - numbers[i - 1] - 1;
                }

                return (int)Math.Min(gaps, int.MaxValue);
            });

        var last = ordered[^1];
        var endSeen = last.HeroStack + last.NetChips == 0 || summary.LastFinishPosition == 1;
        var firstLevel = ordered.Min(h => h.Level);
        bool? startMissing = summary.FirstEntryLateRegistration == false ? firstLevel > 1 : null;
        var entriesMismatch = summary.Entries is { } expected && expected != entries;

        var complete = missing == 0 && stackBreaks == 0 && endSeen && startMissing != true && !entriesMismatch;
        return new TournamentCoverage(
            complete ? CoverageStatus.Complete : CoverageStatus.Partial,
            ordered.Count,
            firstLevel,
            ordered.Max(h => h.Level),
            missing,
            stackBreaks,
            entries,
            startMissing,
            endSeen);
    }
}
