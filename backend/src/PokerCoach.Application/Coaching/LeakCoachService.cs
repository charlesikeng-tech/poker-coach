using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PokerCoach.Application.Leaks;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Application.Coaching;

public enum CoachingFailure
{
    /// <summary>No model configured (no API key).</summary>
    Unavailable,

    /// <summary>The statistic is not (or no longer) a leak with the current figures.</summary>
    NotALeak,

    /// <summary>The user reached the daily number of new explanations.</summary>
    DailyLimitReached,

    /// <summary>The monthly spend cap is reached, for everyone.</summary>
    BudgetExhausted,

    /// <summary>The provider failed or answered outside the schema.</summary>
    ModelFailed,
}

public sealed record CoachingOutcome(StoredExplanation? Explanation, CoachingFailure? Failure)
{
    public static CoachingOutcome Of(StoredExplanation explanation) => new(explanation, null);

    public static CoachingOutcome Fail(CoachingFailure failure) => new(null, failure);
}

/// <summary>
/// Explains one detected leak with the player's own hands (ADR-0008). The figures come from our leak
/// analysis; the model only writes. An explanation is stored and reused until its inputs change.
/// </summary>
public sealed class LeakCoachService(
    LeakService leaks,
    ICoachingStore store,
    ICoachingModel model,
    CoachingOptions options,
    TimeProvider time)
{
    internal const string Purpose = "leak-explanation";

    public async Task<CoachingOutcome> ExplainAsync(
        Guid userId,
        LeakStat stat,
        PokerPosition? position,
        LeakDirection direction,
        TableFormat? format,
        DateTimeOffset? from,
        string language,
        CancellationToken cancellationToken)
    {
        var analysis = await leaks.GetAsync(userId, format, from, null, cancellationToken);
        var leak = analysis.Report.Leaks.FirstOrDefault(l => l.Stat == stat && l.Position == position && l.Direction == direction);
        if (leak is null)
        {
            return CoachingOutcome.Fail(CoachingFailure.NotALeak);
        }

        var fingerprint = Fingerprint(leak, analysis.Format, language, analysis.ReferenceVersion);
        var cached = await store.FindExplanationAsync(userId, fingerprint, cancellationToken);
        if (cached is not null)
        {
            return CoachingOutcome.Of(cached);
        }

        if (!model.IsAvailable)
        {
            return CoachingOutcome.Fail(CoachingFailure.Unavailable);
        }

        var now = time.GetUtcNow();
        var today = new DateTimeOffset(now.Date, TimeSpan.Zero);
        if (await store.CountCallsSinceAsync(userId, Purpose, today, cancellationToken) >= options.DailyExplanationsPerUser)
        {
            return CoachingOutcome.Fail(CoachingFailure.DailyLimitReached);
        }

        // Soft by at most the calls in flight: acceptable for a beta cap (ADR-0008).
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        if (await store.SpendSinceAsync(monthStart, cancellationToken) >= options.MonthlyBudgetUsd)
        {
            return CoachingOutcome.Fail(CoachingFailure.BudgetExhausted);
        }

        var situation = new LeakSituation(stat, position, direction, ReferenceRanges.MinStackBigBlinds, analysis.Format);
        var examples = await store.FindExampleHandsAsync(userId, situation, options.ExampleHands, HeroHandFacts.Version, cancellationToken);
        var labels = examples
            .Select((e, i) =>
            {
                var facts = HandAnalyzer.Analyze(e.Hand);
                return new ExampleHandLabel($"H{i + 1}", e.HandId, e.StartedAt, e.Level, facts.Position, e.HeroCards, facts.StackInBigBlinds);
            })
            .ToList();
        var prompt = new ExplanationPrompt(
            language,
            Facts(leak, analysis.Format),
            examples.Select((e, i) => (labels[i].Ref, HandNarrator.Tell(e))).ToList());

        ModelExplanation answer;
        try
        {
            answer = await model.ExplainAsync(prompt, cancellationToken);
        }
        catch (CoachingModelException)
        {
            return CoachingOutcome.Fail(CoachingFailure.ModelFailed);
        }

        // Spend is recorded even if the answer is then rejected: it was billed.
        await store.RecordUsageAsync(userId, Purpose, answer.Usage, options.CostOf(answer.Usage), cancellationToken);

        // The model may only cite hands we gave it.
        var known = labels.Select(l => l.Ref).ToHashSet(StringComparer.Ordinal);
        var explanation = answer.Explanation with
        {
            Hands = answer.Explanation.Hands.Where(h => known.Contains(h.Ref)).ToList(),
        };
        var cited = explanation.Hands.Select(h => h.Ref).ToHashSet(StringComparer.Ordinal);
        var stored = new StoredExplanation(explanation, labels.Where(l => cited.Contains(l.Ref)).ToList(), answer.Usage.Model, now);
        await store.SaveExplanationAsync(userId, fingerprint, language, stored, cancellationToken);
        return CoachingOutcome.Of(stored);
    }

    /// <summary>The leak in plain facts, all computed by us.</summary>
    internal static string Facts(Leak leak, TableFormat format)
    {
        string Pct(decimal ratio) => (ratio * 100m).ToString("0.#", CultureInfo.InvariantCulture) + " %";
        var where = leak.Position is { } p ? $" from {HandNarrator.Label(p)}" : string.Empty;
        return $"Statistic: {leak.Stat}{where}, {(format == TableFormat.SixMax ? "6-max" : "full-ring")} tables. "
            + $"Player's rate: {Pct(leak.Rate)} over {leak.Opportunities} situations "
            + $"(95 % interval {Pct(leak.IntervalLow)}–{Pct(leak.IntervalHigh)}). "
            + $"Reference for solid low-stakes MTT regulars: {Pct(leak.Range.Min)}–{Pct(leak.Range.Max)}. "
            + $"Direction: {(leak.Direction == LeakDirection.TooHigh ? "too high" : "too low")}; "
            + $"confidence: {(leak.Confidence == LeakConfidence.Confirmed ? "confirmed" : "possible, sample still small")}. "
            + "Only hands with 15+ big blinds are counted.";
    }

    /// <summary>
    /// Bump when the coach's instructions change in a way the player would see (wording, tone): stored
    /// explanations written under the old ones are then written again. 2: informal address, English poker
    /// terms ("leak" never translated).
    /// </summary>
    internal const int ExplanationVersion = 2;

    /// <summary>
    /// What makes an explanation still valid: the leak, its confidence and its rate to the percent. A few
    /// more hands that do not move the rate reuse the same explanation instead of paying for a new one.
    /// </summary>
    internal static string Fingerprint(Leak leak, TableFormat format, string language, int referenceVersion)
    {
        var key = string.Join(
            '|',
            ExplanationVersion.ToString(CultureInfo.InvariantCulture),
            format,
            leak.Stat,
            leak.Position?.ToString() ?? "-",
            leak.Direction,
            leak.Confidence,
            Math.Round(leak.Rate * 100m).ToString(CultureInfo.InvariantCulture),
            referenceVersion.ToString(CultureInfo.InvariantCulture),
            language);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }
}
