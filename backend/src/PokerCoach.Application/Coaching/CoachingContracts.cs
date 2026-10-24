using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Application.Coaching;

/// <summary>Limits and prices of the coaching model (ADR-0008). Configuration section "Coaching".</summary>
public sealed class CoachingOptions
{
    public const string SectionName = "Coaching";

    /// <summary>Hard monthly cap on model spend, all users together (USD).</summary>
    public decimal MonthlyBudgetUsd { get; set; } = 20m;

    /// <summary>New explanations a user can generate per UTC day (cached ones are free).</summary>
    public int DailyExplanationsPerUser { get; set; } = 20;

    /// <summary>New tournament debriefs a user can generate per UTC day.</summary>
    public int DailyDebriefsPerUser { get; set; } = 10;

    /// <summary>New weekly reviews a user can generate per UTC day.</summary>
    public int DailyWeekReviewsPerUser { get; set; } = 5;

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
/// <param name="GameType">Selects the coaching context (MTT or cash game instructions).</param>
public sealed record ExplanationPrompt(string Language, string Facts, IReadOnlyList<(string Ref, string Story)> Hands, GameType GameType = GameType.Tournament);

public sealed record HandNote(string Ref, string Note);

/// <summary>One concrete action step: the situation, what to do, and why it works.</summary>
public sealed record ActionStep(string Spot, string Advice, string Why);

/// <summary>The model's answer, after schema validation.</summary>
public sealed record LeakExplanation(string Summary, string WhyItCosts, IReadOnlyList<ActionStep> Actions, IReadOnlyList<HandNote> Hands, string? NextSessionFocus = null);

public sealed record ModelUsage(string Model, int InputTokens, int OutputTokens, int CacheWriteTokens, int CacheReadTokens);

/// <summary>A validated answer of the model and what it cost.</summary>
public sealed record ModelAnswer<T>(T Value, ModelUsage Usage);

/// <summary>The model's monthly report, after schema validation.</summary>
public sealed record MonthlyReport(string Headline, string Summary, IReadOnlyList<string> Progress, IReadOnlyList<string> WeakPoints, IReadOnlyList<string> NextMonth);

/// <summary>What the model is asked for the monthly report. No player name or id.</summary>
public sealed record MonthlyReportPrompt(string Language, string Facts);

/// <summary>What the model is asked for bankroll coaching. No player name or id.</summary>
/// <param name="Language">BCP-47 code of the answer language (fr, en, es).</param>
/// <param name="Facts">Bankroll figures assembled by <c>BankrollCoachService</c>.</param>
public sealed record BankrollCoachPrompt(string Language, string Facts);

/// <summary>What the model is asked for tournament selection. No player name or id.</summary>
public sealed record TournamentSelectionPrompt(string Language, string Facts);

/// <summary>The model's tournament selection recommendation, after schema validation.</summary>
public sealed record TournamentSelectionAdvice(string Headline, string Summary, IReadOnlyList<string> Strengths, IReadOnlyList<string> Avoid, IReadOnlyList<string> Recommendations);

/// <summary>What the model is asked for the strategy plan. No player name or id.</summary>
/// <param name="GameType">Selects the coaching context (MTT or cash game instructions).</param>
public sealed record StrategyPlanPrompt(string Language, string Facts, GameType GameType = GameType.Tournament);

/// <summary>One buy-in band or stake level's strategic recommendation.</summary>
/// <param name="Approach">"gto", "exploitative", or "mixed".</param>
public sealed record StrategyPlanBand(string Band, string Approach, string Rationale, IReadOnlyList<string> Adjustments, string? PriorityLeak);

/// <summary>The model's strategy plan, after schema validation.</summary>
public sealed record StrategyPlanAdvice(string Headline, string Overview, IReadOnlyList<StrategyPlanBand> Bands, string Progression, IReadOnlyList<string> KeyFocus);

/// <summary>Port to the language model (Infrastructure adapter per provider).</summary>
public interface ICoachingModel
{
    /// <summary>False when not configured (no API key): the feature reports itself unavailable.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// The most a call of this task can be billed, correction attempts included: reserved against the budget
    /// before the call (ADR-0017).
    /// </summary>
    ModelUsage Ceiling(AiTask task);

    /// <exception cref="CoachingModelException">The provider failed or answered outside the schema.</exception>
    Task<ModelAnswer<LeakExplanation>> ExplainAsync(ExplanationPrompt prompt, CancellationToken cancellationToken);

    /// <exception cref="CoachingModelException">The provider failed or answered outside the schema.</exception>
    Task<ModelAnswer<TournamentDebrief>> DebriefTournamentAsync(DebriefPrompt prompt, CancellationToken cancellationToken);

    /// <exception cref="CoachingModelException">The provider failed or answered outside the schema.</exception>
    Task<ModelAnswer<TournamentDebrief>> DebriefCashSessionAsync(DebriefPrompt prompt, CancellationToken cancellationToken);

    /// <exception cref="CoachingModelException">The provider failed or answered outside the schema.</exception>
    Task<ModelAnswer<WeekReview>> ReviewWeekAsync(WeekReviewPrompt prompt, CancellationToken cancellationToken);

    /// <exception cref="CoachingModelException">The provider failed or answered outside the schema.</exception>
    Task<ModelAnswer<BankrollAdvice>> AdviseOnBankrollAsync(BankrollCoachPrompt prompt, CancellationToken cancellationToken);

    /// <exception cref="CoachingModelException">The provider failed or answered outside the schema.</exception>
    Task<ModelAnswer<MonthlyReport>> ReviewMonthAsync(MonthlyReportPrompt prompt, CancellationToken cancellationToken);

    /// <exception cref="CoachingModelException">The provider failed or answered outside the schema.</exception>
    Task<ModelAnswer<TournamentSelectionAdvice>> AdviseTournamentSelectionAsync(TournamentSelectionPrompt prompt, CancellationToken cancellationToken);

    /// <exception cref="CoachingModelException">The provider failed or answered outside the schema.</exception>
    Task<ModelAnswer<StrategyPlanAdvice>> AdviseStrategyPlanAsync(StrategyPlanPrompt prompt, CancellationToken cancellationToken);
}

/// <summary>The provider failed or answered outside the schema.</summary>
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

    /// <param name="usage">What the provider billed before the answer was rejected.</param>
    public CoachingModelException(string message, ModelUsage usage)
        : base(message)
    {
        Usage = usage;
    }

    /// <summary>
    /// Tokens billed by the provider for the failed call; null when unknown (the provider was not reached or did
    /// not answer). An error status is not billed: its usage is zero, not null.
    /// </summary>
    public ModelUsage? Usage { get; init; }
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
/// <param name="Position">Seat named within <paramref name="Format"/> (see OpeningSeat).</param>
public sealed record LeakSituation(LeakStat Stat, PokerPosition? Position, LeakDirection Direction, decimal MinStackBigBlinds, TableFormat Format);

public sealed record StoredExplanation(LeakExplanation Explanation, IReadOnlyList<ExampleHandLabel> Hands, string Model, DateTimeOffset CreatedAt);

/// <summary>How the UI shows an example hand next to the model's note: our facts, not the model's.</summary>
public sealed record ExampleHandLabel(string Ref, Guid HandId, DateTimeOffset StartedAt, int Level, PokerPosition? Position, string? HeroCards, decimal StackInBigBlinds);

public interface ICoachingStore
{
    Task<StoredExplanation?> FindExplanationAsync(Guid userId, string fingerprint, CancellationToken cancellationToken);

    Task SaveExplanationAsync(Guid userId, string fingerprint, string language, StoredExplanation explanation, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExampleHand>> FindExampleHandsAsync(Guid userId, LeakSituation situation, int count, int factsVersion, CancellationToken cancellationToken);
}
