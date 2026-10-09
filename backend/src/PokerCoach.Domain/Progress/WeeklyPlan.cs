using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Domain.Progress;

/// <summary>One thing to work on this week: a leak as it stood when the plan was made.</summary>
/// <param name="BaselineRate">The rate over the baseline period, when the plan was made.</param>
/// <param name="Min">Lower bound of the reference range.</param>
/// <param name="Max">Upper bound of the reference range.</param>
public sealed record PlanPriority(
    LeakStat Stat,
    PokerPosition? Position,
    LeakDirection Direction,
    LeakConfidence Confidence,
    decimal BaselineRate,
    int BaselineOpportunities,
    decimal Min,
    decimal Max);

/// <summary>
/// A week's plan, frozen once made: priorities that moved with every import would leave nothing to work
/// towards. Progress against it is measured live on the week's hands.
/// </summary>
/// <param name="WeekStart">Monday of the week (UTC).</param>
public sealed record WeeklyPlan(DateOnly WeekStart, TableFormat Format, IReadOnlyList<PlanPriority> Priorities, int ReferenceVersion, DateTimeOffset CreatedAt);

public enum PriorityStatus
{
    /// <summary>Too few spots this week to say anything.</summary>
    NotEnoughSpots,

    /// <summary>This week's rate is inside the reference range.</summary>
    InRange,

    /// <summary>Still outside, but closer to the range than the baseline.</summary>
    Improving,

    /// <summary>As far or further than the baseline.</summary>
    OffTrack,
}

/// <param name="Rate">Null without any spot.</param>
public sealed record PriorityProgress(int Made, int Opportunities, decimal? Rate, PriorityStatus Status);

/// <summary>The drill that trains a priority, when the training room has one.</summary>
/// <param name="Positions">Seats to drill; empty: every seat.</param>
public sealed record PriorityDrill(bool Defence, IReadOnlyList<PokerPosition> Positions);

/// <summary>
/// Builds the week's plan from the leak report and says how each priority is going. Weeks start on
/// Monday 00:00 UTC (night sessions in Europe end before it: 01:00–02:00 local).
/// </summary>
public static class WeeklyPlanner
{
    public const int MaxPriorities = 3;

    /// <summary>Two seats of the same statistic at most: three RFI seats would crowd out everything else.</summary>
    public const int MaxPerStat = 2;

    /// <summary>A week's rate is shown below this, but not judged.</summary>
    public const int MinWeekSpots = 10;

    /// <summary>The leaks the plan is built from: recent play, not the player of a year ago.</summary>
    public const int BaselineDays = 90;

    /// <summary>Drill answers to aim for each week.</summary>
    public const int DrillGoal = 100;

    public static DateOnly WeekStart(DateTimeOffset now)
    {
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        var sinceMonday = ((int)day.DayOfWeek + 6) % 7;
        return day.AddDays(-sinceMonday);
    }

    public static DateTimeOffset Start(DateOnly week) => new(week.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <summary>Confirmed leaks first, then the furthest from their range (the report's order).</summary>
    public static IReadOnlyList<PlanPriority> Choose(LeakReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var perStat = new Dictionary<LeakStat, int>();
        var chosen = new List<PlanPriority>();
        foreach (var leak in report.Leaks)
        {
            var taken = perStat.GetValueOrDefault(leak.Stat);
            if (taken >= MaxPerStat)
            {
                continue;
            }

            perStat[leak.Stat] = taken + 1;
            chosen.Add(new PlanPriority(leak.Stat, leak.Position, leak.Direction, leak.Confidence, leak.Rate, leak.Opportunities, leak.Range.Min, leak.Range.Max));
            if (chosen.Count == MaxPriorities)
            {
                break;
            }
        }

        return chosen;
    }

    public static PriorityProgress Assess(PlanPriority priority, int made, int opportunities)
    {
        ArgumentNullException.ThrowIfNull(priority);
        if (opportunities == 0)
        {
            return new PriorityProgress(made, 0, null, PriorityStatus.NotEnoughSpots);
        }

        var rate = Math.Round((decimal)made / opportunities, 4);
        if (opportunities < MinWeekSpots)
        {
            return new PriorityProgress(made, opportunities, rate, PriorityStatus.NotEnoughSpots);
        }

        var status = rate >= priority.Min && rate <= priority.Max
            ? PriorityStatus.InRange
            : (priority.Direction == LeakDirection.TooHigh ? rate < priority.BaselineRate : rate > priority.BaselineRate)
                ? PriorityStatus.Improving
                : PriorityStatus.OffTrack;
        return new PriorityProgress(made, opportunities, rate, status);
    }

    /// <summary>Preflop opening leaks have a drill; the others are worked on hands and the coach.</summary>
    public static PriorityDrill? DrillFor(PlanPriority priority)
    {
        ArgumentNullException.ThrowIfNull(priority);
        return priority.Stat switch
        {
            LeakStat.Rfi => new PriorityDrill(false, priority.Position is { } p ? [p] : []),
            LeakStat.Steal => new PriorityDrill(false, [PokerPosition.Cutoff, PokerPosition.Button, PokerPosition.SmallBlind]),
            LeakStat.Limp or LeakStat.Vpip or LeakStat.Pfr or LeakStat.VpipPfrGap => new PriorityDrill(false, []),
            _ => null,
        };
    }
}
