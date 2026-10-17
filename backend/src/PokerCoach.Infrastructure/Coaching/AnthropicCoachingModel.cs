using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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

    public int MaxOutputTokens { get; set; } = 2_000;

    /// <summary>
    /// Thinking setting for tasks that only write from given facts (leak explanation, weekly review).
    /// Sonnet 5.5 thinks by default, and thinking tokens count toward max_tokens: "between_tools" is its
    /// lowest setting (no up-front thinking without tools). Null sends nothing (models that do not think
    /// by default). Model-specific: change it with <see cref="Model"/>.
    /// </summary>
    public string? WritingThinking { get; set; } = "between_tools";

    /// <summary>Effort for the debrief, which judges decisions and keeps thinking ("low", "medium", "high"; null: model default).</summary>
    public string? DebriefEffort { get; set; } = "medium";

    /// <summary>
    /// Upper bound of one request's input (instructions + facts + hands), for the cost reserved before a call
    /// (ADR-0017). Our prompts stay well below; a larger one is still billed and recorded at its real cost.
    /// </summary>
    public int CeilingInputTokens { get; set; } = 30_000;
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
        You are the coach inside NutsIQ, an analysis tool for online tournament (MTT) players at low
        stakes (5 to 20 euros), mostly 6-max, with antes and bounties.

        """;

    /// <summary>Same voice for every task: the player reads one coach.</summary>
    private const string Voice = """

        Write in the language requested in the message, with its accents written as plain characters
        (é, è, ê, à, ç, ô, û, ñ, á), never as escape sequences or mis-encoded characters. Address the
        player informally ("tu" in French,
        "tú" in Spanish).

        Poker terminology rule: use standard English poker terms in every answer, whatever the requested
        natural language. Translate the explanations, not the poker vocabulary. Keep the standard term
        exactly in English; do not replace it with a translation, a literal equivalent, or a generic
        description. This applies to all poker terms, including terms not listed below.

        Core actions and lines: bet, check, call, raise, fold, limp, open, open-raise, iso-raise, squeeze,
        cold-call, flat, limp-call, limp-reraise, 3-bet, 4-bet, shove, all-in, check-raise,
        continuation bet, c-bet, delayed c-bet, probe bet, donk bet, float, double barrel, triple barrel,
        overbet, value bet, thin value, bluff, semi-bluff, bluff-catcher, hero call, slowplay, trap.
        Strategy and hand terms: range, combo, blocker, equity, fold equity, pot odds, implied odds, outs,
        SPR, nuts, top pair, overpair, underpair, two pair, set, trips, straight, flush, full house,
        flush draw, straight draw, backdoor draw, gutshot, open-ended straight draw, kicker, showdown,
        board texture, dry board, wet board, rainbow, paired board, range advantage, nut advantage,
        capped range, polarized range, merged range, value range, bluff range.
        Streets, positions, and tournament terms: preflop, flop, turn, river, button, cutoff, hijack,
        under the gun, small blind, big blind, MTT, tournament, bubble, ITM, bounty, re-entry, late reg,
        add-on, pay jump, stack, big blind (BB), ante, level, chip EV, ICM, cash game, sit-and-go,
        heads-up, 6-max, full ring, knockout, progressive knockout, mystery bounty, final table.
        Keep abbreviations and established forms exactly as written when they appear in the facts
        (for example VPIP, PFR, RFI, WTSD, W$SD, 3-bet, c-bet, BB). Use the precise poker term that fits
        the spot; do not invent terminology or substitute an inaccurate term.
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
        - actions: two or three concrete things to do differently, each with three parts:
          - spot: one sentence describing the specific situation or trigger.
          - advice: one sentence on the concrete action to take.
          - why: one sentence on why it helps in these games.
        - next_session_focus (optional): one sentence on the one thing to notice or track at the table in the next session. Omit if nothing specific stands out.
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
            ["actions"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("spot", "advice", "why"),
                    ["properties"] = new JsonObject
                    {
                        ["spot"] = new JsonObject { ["type"] = "string" },
                        ["advice"] = new JsonObject { ["type"] = "string" },
                        ["why"] = new JsonObject { ["type"] = "string" },
                    },
                },
            },
            ["next_session_focus"] = new JsonObject { ["type"] = "string" },
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

    /// <summary>Translated poker terms that must not appear in answers, mapped to their correct English equivalent.</summary>
    private static readonly (string Translated, string English)[] TranslatedPokerTerms =
    [
        ("fuite", "leak"), ("fuga", "leak"),
        ("mise de continuation", "c-bet"), ("mise de continuación", "c-bet"), ("apuesta de continuación", "c-bet"), ("apuesta continuada", "c-bet"),
        ("sur-relance", "3-bet"), ("surrelance", "3-bet"), ("re-relance", "3-bet"), ("resubida", "3-bet"),
        ("éventail de mains", "range"), ("rango de manos", "range"),
        ("tapis", "all-in"), ("farol", "bluff"), ("semi-farol", "semi-bluff"),
        ("mise de valeur", "value bet"), ("apuesta de valor", "value bet"),
        ("carte gratuite", "free card"), ("carta gratis", "free card"),
        ("grosse blinde", "big blind"), ("gran ciega", "big blind"), ("petite blinde", "small blind"), ("ciega pequeña", "small blind"),
        ("hors position", "out of position"), ("fuera de posición", "out of position"),
        ("limper", "limp"), ("limpear", "limp"),
    ];

    private const int MaxCorrectionAttempts = 1;

    /// <summary>Thinking and answer share this cap: room for the reasoning on eight hands plus the text.</summary>
    private const int DebriefMaxOutputTokens = 12_000;

    private const int ReviewMaxOutputTokens = 2_000;

    /// <summary>Writing from given facts needs no up-front thinking; judging decisions benefits from it.</summary>
    private enum Reasoning
    {
        Writing,
        Judging,
    }

    private static readonly Dictionary<string, MomentVerdict> Verdicts = new(StringComparer.Ordinal)
    {
        ["well_played"] = MomentVerdict.WellPlayed,
        ["mistake"] = MomentVerdict.Mistake,
        ["variance"] = MomentVerdict.Variance,
        ["standard"] = MomentVerdict.Standard,
    };

    public bool IsAvailable => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    /// <summary>
    /// Every attempt at full output, and every input at the cache-write rate (the dearest): a correction resends the
    /// prompt plus the rejected answer.
    /// </summary>
    public ModelUsage Ceiling(AiTask task)
    {
        var settings = options.Value;
        var output = task switch
        {
            AiTask.TournamentDebrief or AiTask.CashDebrief => DebriefMaxOutputTokens,
            AiTask.WeekReview => ReviewMaxOutputTokens,
            _ => settings.MaxOutputTokens,
        };
        const int attempts = 1 + MaxCorrectionAttempts;
        return new ModelUsage(
            settings.Model,
            InputTokens: 0,
            OutputTokens: attempts * output,
            CacheWriteTokens: (attempts * settings.CeilingInputTokens) + (MaxCorrectionAttempts * output),
            CacheReadTokens: 0);
    }

    public Task<ModelAnswer<LeakExplanation>> ExplainAsync(ExplanationPrompt prompt, CancellationToken cancellationToken) =>
        LoggedAsync("leak-explanation", () => ExplainCoreAsync(prompt, cancellationToken));

    public Task<ModelAnswer<TournamentDebrief>> DebriefTournamentAsync(DebriefPrompt prompt, CancellationToken cancellationToken) =>
        LoggedAsync("tournament-debrief", () => DebriefCoreAsync(prompt, cancellationToken));

    public Task<ModelAnswer<WeekReview>> ReviewWeekAsync(WeekReviewPrompt prompt, CancellationToken cancellationToken) =>
        LoggedAsync("week-review", () => ReviewCoreAsync(prompt, cancellationToken));

    /// <summary>Every rejected answer is logged with its reason: otherwise the player sees "try again" and nobody knows why.</summary>
    private async Task<T> LoggedAsync<T>(string task, Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (CoachingModelException exception)
        {
            LogRejected(logger, task, exception.Message);
            throw;
        }
    }

    private async Task<ModelAnswer<LeakExplanation>> ExplainCoreAsync(ExplanationPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var user = $"Answer language: {prompt.Language}.\n\nLeak:\n{prompt.Facts}\n\n"
            + string.Join("\n\n", prompt.Hands.Select(h => $"Hand {h.Ref}:\n{h.Story}"));
        var (answer, usage) = await CallAsync<Answer>(Instructions, Schema, user, options.Value.MaxOutputTokens, Reasoning.Writing, cancellationToken);
        if (string.IsNullOrWhiteSpace(answer.Summary) || string.IsNullOrWhiteSpace(answer.WhyItCosts))
        {
            throw new CoachingModelException("The model answer misses required fields.", usage);
        }

        var actions = (answer.Actions ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a.Spot) && !string.IsNullOrWhiteSpace(a.Advice) && !string.IsNullOrWhiteSpace(a.Why))
            .Select(a => new ActionStep(a.Spot!.Trim(), a.Advice!.Trim(), a.Why!.Trim()))
            .Take(3)
            .ToList();
        return new ModelAnswer<LeakExplanation>(
            new LeakExplanation(
                answer.Summary.Trim(),
                answer.WhyItCosts.Trim(),
                actions,
                (answer.Hands ?? []).Where(h => h.Ref is not null && h.Note is not null).Select(h => new HandNote(h.Ref!, h.Note!.Trim())).Take(3).ToList(),
                string.IsNullOrWhiteSpace(answer.NextSessionFocus) ? null : answer.NextSessionFocus.Trim()),
            usage);
    }

    private async Task<ModelAnswer<TournamentDebrief>> DebriefCoreAsync(DebriefPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var user = $"Answer language: {prompt.Language}.\n\n{prompt.Facts}\n\n"
            + string.Join("\n\n", prompt.Moments.Select(m => $"Key moment {m.Ref}:\n{m.Story}"));
        var (answer, usage) = await CallAsync<DebriefAnswer>(DebriefInstructions, DebriefSchema, user, DebriefMaxOutputTokens, Reasoning.Judging, cancellationToken);
        if (string.IsNullOrWhiteSpace(answer.Headline) || string.IsNullOrWhiteSpace(answer.Story))
        {
            throw new CoachingModelException("The model answer misses required fields.", usage);
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

    private async Task<ModelAnswer<WeekReview>> ReviewCoreAsync(WeekReviewPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var user = $"Answer language: {prompt.Language}.\n\n{prompt.Facts}";
        var (answer, usage) = await CallAsync<ReviewAnswer>(ReviewInstructions, ReviewSchema, user, ReviewMaxOutputTokens, Reasoning.Writing, cancellationToken);
        if (string.IsNullOrWhiteSpace(answer.Headline) || string.IsNullOrWhiteSpace(answer.Summary))
        {
            throw new CoachingModelException("The model answer misses required fields.", usage);
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

    /// <summary>Calls the Messages API and makes one correction attempt for a rejected answer.</summary>
    private async Task<(T Answer, ModelUsage Usage)> CallAsync<T>(
        string instructions,
        JsonObject schema,
        string user,
        int maxTokens,
        Reasoning reasoning,
        CancellationToken cancellationToken,
        int correctionAttempts = 0)
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
        if (reasoning == Reasoning.Writing && settings.WritingThinking is { Length: > 0 } thinking)
        {
            body["thinking"] = new JsonObject { ["type"] = thinking };
        }
        else if (reasoning == Reasoning.Judging && settings.DebriefEffort is { Length: > 0 } effort)
        {
            ((JsonObject)body["output_config"]!)["effort"] = effort;
        }

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
                // The provider's error type and message say what to fix (bad schema, no credit, unknown
                // model…). Never the request body: it holds the player's hands.
                var (type, message) = await ReadErrorAsync(httpResponse, cancellationToken);
                httpResponse.Headers.TryGetValues("request-id", out var requestIds);
                LogFailure(logger, (int)httpResponse.StatusCode, type, message, requestIds?.FirstOrDefault() ?? "-");
                // An error status is not billed by the provider.
                throw new CoachingModelException($"The model provider answered {(int)httpResponse.StatusCode}.", new ModelUsage(settings.Model, 0, 0, 0, 0));
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
        catch (JsonException exception)
        {
            throw new CoachingModelException("The model provider's answer is not readable.", exception);
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
            throw new CoachingModelException($"The model stopped with '{response.StopReason}'.", usage);
        }

        var text = response.Content?.FirstOrDefault(c => c.Type == "text")?.Text
            ?? throw new CoachingModelException("No text in the model answer.", usage);
        try
        {
            var answer = JsonNode.Parse(text) ?? throw new CoachingModelException("Empty model answer.", usage);
            return (Repaired(answer).Deserialize<T>(JsonOptions) ?? throw new CoachingModelException("Empty model answer.", usage), usage);
        }
        catch (AnswerRejectedException exception) when (correctionAttempts < MaxCorrectionAttempts)
        {
            LogCorrectionAttempt(logger, exception.Message);
            var correction = user
                + "\n\nYour previous answer was rejected. " + exception.Message
                + "\nReplace every detected translated poker term with the correct standard English poker term. "
                + "Do not repeat any detected translated term. Rewrite the complete answer in valid UTF-8. "
                + "Preserve the meaning and facts; do not introduce new facts. Return only the corrected JSON."
                + "\nPrevious answer to correct:\n" + text;
            try
            {
                var (correctedAnswer, correctionUsage) = await CallAsync<T>(
                    instructions,
                    schema,
                    correction,
                    maxTokens,
                    reasoning,
                    cancellationToken,
                    correctionAttempts + 1);
                return (correctedAnswer, CombineUsage(usage, correctionUsage));
            }
            catch (CoachingModelException failed)
            {
                // Both attempts were billed; if the second one's billing is unknown, so is the total.
                throw new CoachingModelException(failed.Message, failed) { Usage = failed.Usage is { } second ? CombineUsage(usage, second) : null };
            }
        }
        catch (AnswerRejectedException exception)
        {
            throw new CoachingModelException(exception.Message, usage);
        }
        catch (JsonException exception)
        {
            throw new CoachingModelException("The model answer is not valid JSON.", exception) { Usage = usage };
        }
    }

    /// <summary>
    /// Every string of the answer through <see cref="ModelText"/>: mojibake that maps back to UTF-8 is
    /// repaired; what remains garbled rejects the answer rather than showing it to the player.
    /// </summary>
    private static JsonNode Repaired(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj.ToList())
                {
                    if (value is not null)
                    {
                        obj[key] = Repaired(value.DeepClone());
                    }
                }

                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is { } item)
                    {
                        array[i] = Repaired(item.DeepClone());
                    }
                }

                return array;
            case JsonValue value when value.TryGetValue<string>(out var text):
                var repaired = ModelText.Repair(text);
                if (ModelText.IsGarbled(repaired))
                {
                    throw new AnswerRejectedException("The model answer has garbled characters (mojibake).");
                }

                var translatedTerms = FindTranslatedPokerTerms(repaired);
                if (translatedTerms.Count > 0)
                {
                    var termList = translatedTerms.Select(t => $"'{t.Translated}' (use '{t.English}' instead)");
                    throw new AnswerRejectedException(
                        $"Translated poker term(s) detected: {string.Join(", ", termList)}. "
                        + "Use the standard English poker term(s) instead.");
                }

                return JsonValue.Create(repaired);
            default:
                return node;
        }
    }

    internal static bool ContainsTranslatedPokerTerm(string text) => FindTranslatedPokerTerms(text).Count > 0;

    private static List<(string Translated, string English)> FindTranslatedPokerTerms(string text) =>
        TranslatedPokerTerms
            .Where(t => Regex.IsMatch(
                text,
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(t.Translated)}(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToList();

    private static ModelUsage CombineUsage(ModelUsage first, ModelUsage second) =>
        new(
            second.Model,
            first.InputTokens + second.InputTokens,
            first.OutputTokens + second.OutputTokens,
            first.CacheWriteTokens + second.CacheWriteTokens,
            first.CacheReadTokens + second.CacheReadTokens);

    /// <summary>Anthropic errors are <c>{"type":"error","error":{"type":"…","message":"…"}}</c>; anything else is reported as unknown.</summary>
    private static async Task<(string Type, string Message)> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        const int MaxMessageLength = 500;
        try
        {
            var error = (await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions, cancellationToken))?.Error;
            var message = error?.Message ?? "-";
            return (error?.Type ?? "unknown", message.Length > MaxMessageLength ? message[..MaxMessageLength] : message);
        }
        catch (JsonException)
        {
            return ("unknown", "-");
        }
        catch (NotSupportedException)
        {
            return ("unknown", "-");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Coaching model call failed with HTTP {Status}: {ErrorType}: {ErrorMessage} (request {RequestId}).")]
    private static partial void LogFailure(ILogger logger, int status, string errorType, string errorMessage, string requestId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Coaching {Task} answer rejected: {Reason}")]
    private static partial void LogRejected(ILogger logger, string task, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retrying coaching answer after validation rejection: {Reason}")]
    private static partial void LogCorrectionAttempt(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Coaching model {Model}: {InputTokens} in, {OutputTokens} out, {CacheReadTokens} cached, {ElapsedMs} ms.")]
    private static partial void LogCall(ILogger logger, string model, int inputTokens, int outputTokens, int cacheReadTokens, double elapsedMs);

    private sealed record MessagesResponse(string? Model, string? StopReason, IReadOnlyList<ContentBlock>? Content, UsageBlock? Usage);

    private sealed record ContentBlock(string Type, string? Text);

    private sealed record ErrorResponse(ErrorDetail? Error);

    private sealed record ErrorDetail(string? Type, string? Message);

    private sealed record UsageBlock(
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens,
        [property: JsonPropertyName("cache_creation_input_tokens")] int? CacheCreationInputTokens,
        [property: JsonPropertyName("cache_read_input_tokens")] int? CacheReadInputTokens);

    private sealed class AnswerRejectedException(string message) : Exception(message)
    {
    }

    private sealed record Answer(string Summary, string WhyItCosts, IReadOnlyList<AnswerAction>? Actions, IReadOnlyList<AnswerHand>? Hands, string? NextSessionFocus);

    private sealed record AnswerAction(string? Spot, string? Advice, string? Why);

    private sealed record AnswerHand(string? Ref, string? Note);

    private sealed record DebriefAnswer(string Headline, string Story, IReadOnlyList<DebriefAnswerMoment>? Moments, IReadOnlyList<string>? Strengths, IReadOnlyList<string>? WorkOn);

    private sealed record DebriefAnswerMoment(string? Ref, string? Verdict, string? Note);

    private sealed record ReviewAnswer(string Headline, string Summary, IReadOnlyList<AnswerHand>? Priorities, IReadOnlyList<string>? NextSteps);
}
