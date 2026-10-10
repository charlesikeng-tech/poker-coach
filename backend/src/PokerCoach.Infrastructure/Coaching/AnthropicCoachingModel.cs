using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokerCoach.Application.Coaching;

namespace PokerCoach.Infrastructure.Coaching;

/// <summary>Configuration section "Coaching:Anthropic". The key comes from user-secrets or a secret store, never a file.</summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Coaching:Anthropic";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-sonnet-5-5";

    public Uri BaseUrl { get; set; } = new("https://api.anthropic.com/");

    public int MaxOutputTokens { get; set; } = 1_500;
}

/// <summary>
/// Claude Messages API over plain HTTP (no SDK: one endpoint, and the request stays readable). The fixed
/// instructions are a cached system block; the answer is constrained by a JSON schema (structured outputs).
/// </summary>
internal sealed partial class AnthropicCoachingModel(
    HttpClient http,
    IOptions<AnthropicOptions> options,
    ILogger<AnthropicCoachingModel> logger) : ICoachingModel
{
    private const string Context = """
        You are the coach inside Poker Coach, an analysis tool for online tournament (MTT) players at low
        stakes (5 to 20 euros), mostly 6-max, with antes and bounties.

        """;

    /// <summary>Same voice for every task: the player reads one coach.</summary>
    private const string Voice = """

        Write in the language requested in the message, addressing the player informally ("tu" in French,
        "tú" in Spanish). Keep poker terms in English as players say them in every language: leak, c-bet,
        3-bet, range, steal, check-raise, all-in, bluff. Never translate "leak" (not "fuite", not "fuga").
        """;

    private const string Instructions = Context + """
        You receive one leak: a statistic where the player differs from solid regulars, with figures that
        were computed by the software, and a few of the player's own hands where the leak shows.

        Rules:
        - Use only the figures given. Never compute, estimate or invent a statistic, a percentage or a range.
        - Talk about the hands only through the facts given (positions, stacks in big blinds, actions, board,
          the hero's cards). Opponents are known only by position. Do not guess cards that were not shown.
        - Reference hands only by their id (H1, H2...), at most three, choosing the most instructive ones.
        - Be concrete and practical, for a recreational player who wants to improve: no jargon without a
          short explanation, no moralising, no generic advice that would fit any leak.
        - Tournament context matters: stack depth in big blinds, antes, bounties, pay jumps.
        - If the sample is small ("possible"), say so plainly and frame the advice as something to check.
        """ + Voice + """
        Fill the fields:
        - summary: one or two sentences, what the leak is for this player.
        - why_it_costs: two or three sentences, why it loses chips or money in these games.
        - actions: two or three short, specific things to do differently.
        - hands: for each cited hand, one or two sentences on what to do instead and why.
        """;

    private static readonly JsonObject Schema = new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("summary", "why_it_costs", "actions", "hands"),
        ["properties"] = new JsonObject
        {
            ["summary"] = new JsonObject { ["type"] = "string" },
            ["why_it_costs"] = new JsonObject { ["type"] = "string" },
            ["actions"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            ["hands"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("ref", "note"),
                    ["properties"] = new JsonObject
                    {
                        ["ref"] = new JsonObject { ["type"] = "string" },
                        ["note"] = new JsonObject { ["type"] = "string" },
                    },
                },
            },
        },
    };

    private const string DebriefInstructions = Context + """
        You receive the facts of one tournament the player played, computed by the software, and the stories
        of its key moments: the hands that won or lost at least a quarter of the stack.

        Rules:
        - Use only the figures given. Never compute, estimate or invent a statistic, a percentage, a finish
          or an amount of money. If the result is unknown, do not talk about the finish or the money.
        - Talk about the hands only through the facts given (positions, stacks in big blinds, actions, board,
          cards). Opponents are known only by position, and their cards only if they were shown.
        - One tournament is a small sample: its statistics describe this tournament, they never prove a leak.
          Link a moment to one of the player's long-term leaks only when the hand clearly shows it.
        - Judge decisions, not results: a lost all-in can be well played, a won pot can be a mistake.
        - Verdict "variance" only for a preflop all-in whose computed equity is given and whose result went
          against it. When the facts are not enough to judge a decision, use "standard".
        - Be concrete and practical, for a recreational player who wants to improve: no jargon without a
          short explanation, no moralising, no generic advice that would fit any tournament.
        - Tournament context matters: stack depth in big blinds, antes, bounties, pay jumps.
        """ + Voice + """
        Fill the fields:
        - headline: one sentence that sums up the tournament.
        - story: three to five sentences on how the tournament went, from the stack and the key moments.
        - moments: the key moments worth a comment (at most six), each with its ref (M1, M2...), a verdict
          and a note of one or two sentences: what happened and what to do next time.
        - strengths: one to three things the player did well, tied to the facts given.
        - work_on: one to three concrete things to work on, tied to the facts given.
        """;

    private const string ReviewInstructions = Context + """
        You receive the player's week: the plan's priorities (leaks chosen on Monday), how each one moved on
        the week's hands, the drills done and the results, all computed by the software.

        Rules:
        - Use only the figures given. Never compute, estimate or invent a statistic or a percentage.
        - The status given for each priority is the measurement: do not contradict it. "Not judged" means too
          few spots: encourage playing or drilling that spot, draw no conclusion.
        - Money over a week is mostly variance: never judge the player's level or the plan from it.
        - If the week is in progress, write a mid-week check and what to do in the days left.
        - Be concrete and encouraging without flattery: name what improved, what did not, and the next step.
        """ + Voice + """
        Fill the fields:
        - headline: one sentence on the week.
        - summary: two to four sentences on the week against the plan.
        - priorities: for each priority (ref P1, P2...), one or two sentences: what its figure says and the
          next concrete step (a drill, a spot to watch at the table).
        - next_steps: two or three concrete actions for the coming days.
        """;

    private static readonly JsonObject Text = new() { ["type"] = "string" };

    private static readonly JsonObject Texts = new() { ["type"] = "array", ["items"] = Text.DeepClone() };

    private static readonly JsonObject DebriefSchema = new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("headline", "story", "moments", "strengths", "work_on"),
        ["properties"] = new JsonObject
        {
            ["headline"] = Text.DeepClone(),
            ["story"] = Text.DeepClone(),
            ["moments"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("ref", "verdict", "note"),
                    ["properties"] = new JsonObject
                    {
                        ["ref"] = Text.DeepClone(),
                        ["verdict"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("well_played", "mistake", "variance", "standard") },
                        ["note"] = Text.DeepClone(),
                    },
                },
            },
            ["strengths"] = Texts.DeepClone(),
            ["work_on"] = Texts.DeepClone(),
        },
    };

    private static readonly JsonObject ReviewSchema = new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("headline", "summary", "priorities", "next_steps"),
        ["properties"] = new JsonObject
        {
            ["headline"] = Text.DeepClone(),
            ["summary"] = Text.DeepClone(),
            ["priorities"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("ref", "note"),
                    ["properties"] = new JsonObject { ["ref"] = Text.DeepClone(), ["note"] = Text.DeepClone() },
                },
            },
            ["next_steps"] = Texts.DeepClone(),
        },
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>A debrief comments up to eight hands: more room than a leak explanation.</summary>
    private const int DebriefMaxOutputTokens = 2_500;

    private const int ReviewMaxOutputTokens = 1_500;

    private static readonly Dictionary<string, MomentVerdict> Verdicts = new(StringComparer.Ordinal)
    {
        ["well_played"] = MomentVerdict.WellPlayed,
        ["mistake"] = MomentVerdict.Mistake,
        ["variance"] = MomentVerdict.Variance,
        ["standard"] = MomentVerdict.Standard,
    };

    public bool IsAvailable => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public async Task<ModelExplanation> ExplainAsync(ExplanationPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var user = $"Answer language: {prompt.Language}.\n\nLeak:\n{prompt.Facts}\n\n"
            + string.Join("\n\n", prompt.Hands.Select(h => $"Hand {h.Ref}:\n{h.Story}"));
        var (answer, usage) = await CallAsync<Answer>(Instructions, Schema, user, options.Value.MaxOutputTokens, cancellationToken);
        if (string.IsNullOrWhiteSpace(answer.Summary) || string.IsNullOrWhiteSpace(answer.WhyItCosts))
        {
            throw new CoachingModelException("The model answer misses required fields.");
        }

        return new ModelExplanation(
            new LeakExplanation(
                answer.Summary.Trim(),
                answer.WhyItCosts.Trim(),
                Clean(answer.Actions, 5),
                (answer.Hands ?? []).Where(h => h.Ref is not null && h.Note is not null).Select(h => new HandNote(h.Ref!, h.Note!.Trim())).Take(3).ToList()),
            usage);
    }

    public async Task<ModelAnswer<TournamentDebrief>> DebriefTournamentAsync(DebriefPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var user = $"Answer language: {prompt.Language}.\n\n{prompt.Facts}\n\n"
            + string.Join("\n\n", prompt.Moments.Select(m => $"Key moment {m.Ref}:\n{m.Story}"));
        var (answer, usage) = await CallAsync<DebriefAnswer>(DebriefInstructions, DebriefSchema, user, DebriefMaxOutputTokens, cancellationToken);
        if (string.IsNullOrWhiteSpace(answer.Headline) || string.IsNullOrWhiteSpace(answer.Story))
        {
            throw new CoachingModelException("The model answer misses required fields.");
        }

        var moments = (answer.Moments ?? [])
            .Where(m => m.Ref is not null && !string.IsNullOrWhiteSpace(m.Note) && Verdicts.ContainsKey(m.Verdict ?? string.Empty))
            .Select(m => new MomentNote(m.Ref!, Verdicts[m.Verdict!], m.Note!.Trim()))
            .Take(8)
            .ToList();
        return new ModelAnswer<TournamentDebrief>(
            new TournamentDebrief(answer.Headline.Trim(), answer.Story.Trim(), moments, Clean(answer.Strengths, 3), Clean(answer.WorkOn, 3)),
            usage);
    }

    public async Task<ModelAnswer<WeekReview>> ReviewWeekAsync(WeekReviewPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var user = $"Answer language: {prompt.Language}.\n\n{prompt.Facts}";
        var (answer, usage) = await CallAsync<ReviewAnswer>(ReviewInstructions, ReviewSchema, user, ReviewMaxOutputTokens, cancellationToken);
        if (string.IsNullOrWhiteSpace(answer.Headline) || string.IsNullOrWhiteSpace(answer.Summary))
        {
            throw new CoachingModelException("The model answer misses required fields.");
        }

        return new ModelAnswer<WeekReview>(
            new WeekReview(
                answer.Headline.Trim(),
                answer.Summary.Trim(),
                (answer.Priorities ?? []).Where(p => p.Ref is not null && !string.IsNullOrWhiteSpace(p.Note)).Select(p => new PriorityNote(p.Ref!, p.Note!.Trim())).Take(3).ToList(),
                Clean(answer.NextSteps, 3)),
            usage);
    }

    private static List<string> Clean(IReadOnlyList<string>? items, int max) =>
        (items ?? []).Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).Take(max).ToList();

    /// <summary>One Messages API call: cached instructions, the user's facts, an answer bound to the schema.</summary>
    private async Task<(T Answer, ModelUsage Usage)> CallAsync<T>(string instructions, JsonObject schema, string user, int maxTokens, CancellationToken cancellationToken)
        where T : class
    {
        var settings = options.Value;
        var body = new JsonObject
        {
            ["model"] = settings.Model,
            ["max_tokens"] = maxTokens,
            ["system"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = instructions,
                ["cache_control"] = new JsonObject { ["type"] = "ephemeral" },
            }),
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = user }),
            ["output_config"] = new JsonObject
            {
                ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = schema.DeepClone() },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.BaseUrl, "v1/messages"))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("x-api-key", settings.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        MessagesResponse response;
        var started = TimeProvider.System.GetTimestamp();
        try
        {
            using var httpResponse = await http.SendAsync(request, cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                // The body may echo the request: log the status only.
                LogFailure(logger, (int)httpResponse.StatusCode);
                throw new CoachingModelException($"The model provider answered {(int)httpResponse.StatusCode}.");
            }

            response = await httpResponse.Content.ReadFromJsonAsync<MessagesResponse>(JsonOptions, cancellationToken)
                ?? throw new CoachingModelException("Empty answer from the model provider.");
        }
        catch (HttpRequestException exception)
        {
            throw new CoachingModelException("The model provider could not be reached.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CoachingModelException("The model provider timed out.", exception);
        }

        var usage = new ModelUsage(
            response.Model ?? settings.Model,
            response.Usage?.InputTokens ?? 0,
            response.Usage?.OutputTokens ?? 0,
            response.Usage?.CacheCreationInputTokens ?? 0,
            response.Usage?.CacheReadInputTokens ?? 0);
        LogCall(logger, usage.Model, usage.InputTokens, usage.OutputTokens, usage.CacheReadTokens, TimeProvider.System.GetElapsedTime(started).TotalMilliseconds);

        if (response.StopReason is not "end_turn")
        {
            throw new CoachingModelException($"The model stopped with '{response.StopReason}'.");
        }

        var text = response.Content?.FirstOrDefault(c => c.Type == "text")?.Text
            ?? throw new CoachingModelException("No text in the model answer.");
        try
        {
            return (JsonSerializer.Deserialize<T>(text, JsonOptions) ?? throw new CoachingModelException("Empty model answer."), usage);
        }
        catch (JsonException exception)
        {
            throw new CoachingModelException("The model answer is not valid JSON.", exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Coaching model call failed with HTTP {Status}.")]
    private static partial void LogFailure(ILogger logger, int status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Coaching model {Model}: {InputTokens} in, {OutputTokens} out, {CacheReadTokens} cached, {ElapsedMs} ms.")]
    private static partial void LogCall(ILogger logger, string model, int inputTokens, int outputTokens, int cacheReadTokens, double elapsedMs);

    private sealed record MessagesResponse(string? Model, string? StopReason, IReadOnlyList<ContentBlock>? Content, UsageBlock? Usage);

    private sealed record ContentBlock(string Type, string? Text);

    private sealed record UsageBlock(
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens,
        [property: JsonPropertyName("cache_creation_input_tokens")] int? CacheCreationInputTokens,
        [property: JsonPropertyName("cache_read_input_tokens")] int? CacheReadInputTokens);

    private sealed record Answer(string Summary, string WhyItCosts, IReadOnlyList<string>? Actions, IReadOnlyList<AnswerHand>? Hands);

    private sealed record AnswerHand(string? Ref, string? Note);

    private sealed record DebriefAnswer(string Headline, string Story, IReadOnlyList<DebriefAnswerMoment>? Moments, IReadOnlyList<string>? Strengths, IReadOnlyList<string>? WorkOn);

    private sealed record DebriefAnswerMoment(string? Ref, string? Verdict, string? Note);

    private sealed record ReviewAnswer(string Headline, string Summary, IReadOnlyList<AnswerHand>? Priorities, IReadOnlyList<string>? NextSteps);
}
