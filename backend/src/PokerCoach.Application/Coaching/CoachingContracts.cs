using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;

namespace PokerCoach.Application.Coaching;

/// <summary>Limits and prices of the coaching model (ADR-0008). Configuration section "Coaching".</summary>
public sealed class CoachingOptions
{
    public const string SectionName = "Coaching";

    /// <summary>Hard monthly cap on model spend, all users together (USD).</summary>
    public decimal MonthlyBudgetUsd { get; set; } = 20m;

    /// <summary>New explanations a user can generate per UTC day (cached ones are free).</summary>
    public int DailyExplanationsPerUser { get; set; } = 20;

    /// <summary>Example hands given to the model per explanation.</summary>
    public int ExampleHands { get; set; } = 5;

    // List prices, USD per million tokens, for the configured model (Claude Sonnet 5.5 on 2026-10-09).
    public decimal InputPricePerMillion { get; set; } = 2m;

    public decimal OutputPricePerMillion { get; set; } = 10m;

    public decimal CacheWritePricePerMillion { get; set; } = 2.5m;

    public decimal CacheReadPricePerMillion { get; set; } = 0.10m;

    public decimal CostOf(ModelUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        return ((usage.InputTokens * InputPricePerMillion)
                + (usage.OutputTokens * OutputPricePerMillion)
                + (usage.CacheWriteTokens * CacheWritePricePerMillion)
                + (usage.CacheReadTokens * CacheReadPricePerMillion)) / 1_000_000m;
    }
}

/// <summary>What the model is asked. Contains no player name, pseudonym or id (ADR-0008, principle 3).</summary>
/// <param name="Language">BCP-47 code of the answer language (fr, en, es).</param>
/// <param name="Facts">The leak and its figures, written by us.</param>
/// <param name="Hands">Example hands: a short reference ("H1") and their anonymized story.</param>
public sealed record ExplanationPrompt(string Language, string Facts, IReadOnlyList<(string Ref, string Story)> Hands);

public sealed record HandNote(string Ref, string Note);

/// <summary>The model's answer, after schema validation.</summary>
public sealed record LeakExplanation(string Summary, string WhyItCosts, IReadOnlyList<string> Actions, IReadOnlyList<HandNote> Hands);

public sealed record ModelUsage(string Model, int InputTokens, int OutputTokens, int CacheWriteTokens, int CacheReadTokens);

public sealed record ModelExplanation(LeakExplanation Explanation, ModelUsage Usage);

/// <summary>Port to the language model (Infrastructure adapter per provider).</summary>
public interface ICoachingModel
{
    /// <summary>False when not configured (no API key): the feature reports itself unavailable.</summary>
    bool IsAvailable { get; }

    /// <exception cref="CoachingModelException">The provider failed or answered outside the schema.</exception>
    Task<ModelExplanation> ExplainAsync(ExplanationPrompt prompt, CancellationToken cancellationToken);
}

public sealed class CoachingModelException : Exception
{
    public CoachingModelException(string message)
        : base(message)
    {
    }

    public CoachingModelException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public CoachingModelException()
    {
    }
}

/// <summary>A hand that illustrates a leak, with what the story needs beyond the analysis input.</summary>
/// <param name="HeroCards">Text form ("AhKd"); null if unknown.</param>
public sealed record ExampleHand(
    Guid HandId,
    DateTimeOffset StartedAt,
    int Level,
    long? Ante,
    HandForAnalysis Hand,
    IReadOnlyList<Card> Board,
    string? HeroCards);

/// <summary>The spot a leak is about: hands where it happened (too high) or could have and did not (too low).</summary>
public sealed record LeakSituation(LeakStat Stat, PokerPosition? Position, LeakDirection Direction, decimal MinStackBigBlinds);

public sealed record StoredExplanation(LeakExplanation Explanation, IReadOnlyList<ExampleHandLabel> Hands, string Model, DateTimeOffset CreatedAt);

/// <summary>How the UI shows an example hand next to the model's note: our facts, not the model's.</summary>
public sealed record ExampleHandLabel(string Ref, Guid HandId, DateTimeOffset StartedAt, int Level, PokerPosition? Position, string? HeroCards, decimal StackInBigBlinds);

public interface ICoachingStore
{
    Task<StoredExplanation?> FindExplanationAsync(Guid userId, string fingerprint, CancellationToken cancellationToken);

    Task SaveExplanationAsync(Guid userId, string fingerprint, string language, StoredExplanation explanation, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExampleHand>> FindExampleHandsAsync(Guid userId, LeakSituation situation, int count, int factsVersion, CancellationToken cancellationToken);

    Task RecordUsageAsync(Guid? userId, string purpose, ModelUsage usage, decimal costUsd, CancellationToken cancellationToken);

    Task<decimal> SpendSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);

    Task<int> CountCallsSinceAsync(Guid userId, string purpose, DateTimeOffset since, CancellationToken cancellationToken);
}
