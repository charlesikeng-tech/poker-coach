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
    private const string Instructions = """
        You are the coach inside Poker Coach, an analysis tool for online tournament (MTT) players at low
        stakes (5 to 20 euros), mostly 6-max, with antes and bounties.

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

        Write in the language requested in the message. Fill the fields:
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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public bool IsAvailable => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public async Task<ModelExplanation> ExplainAsync(ExplanationPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var settings = options.Value;

        var user = $"Answer language: {prompt.Language}.\n\nLeak:\n{prompt.Facts}\n\n"
            + string.Join("\n\n", prompt.Hands.Select(h => $"Hand {h.Ref}:\n{h.Story}"));
        var body = new JsonObject
        {
            ["model"] = settings.Model,
            ["max_tokens"] = settings.MaxOutputTokens,
            ["system"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = Instructions,
                ["cache_control"] = new JsonObject { ["type"] = "ephemeral" },
            }),
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = user }),
            ["output_config"] = new JsonObject
            {
                ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = Schema.DeepClone() },
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
        Answer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<Answer>(text, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new CoachingModelException("The model answer is not valid JSON.", exception);
        }

        if (answer is null || string.IsNullOrWhiteSpace(answer.Summary) || string.IsNullOrWhiteSpace(answer.WhyItCosts))
        {
            throw new CoachingModelException("The model answer misses required fields.");
        }

        return new ModelExplanation(
            new LeakExplanation(
                answer.Summary.Trim(),
                answer.WhyItCosts.Trim(),
                (answer.Actions ?? []).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).Take(5).ToList(),
                (answer.Hands ?? []).Where(h => h.Ref is not null && h.Note is not null).Select(h => new HandNote(h.Ref!, h.Note!.Trim())).Take(3).ToList()),
            usage);
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
}
