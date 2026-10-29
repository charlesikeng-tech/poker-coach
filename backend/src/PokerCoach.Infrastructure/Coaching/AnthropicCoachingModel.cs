using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokerCoach.Application.Coaching;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Infrastructure.Coaching;

/// <summary>Configuration section "Coaching:Anthropic". The key comes from user-secrets or a secret store, never a file.</summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Coaching:Anthropic";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-sonnet-5-5";

    /// <summary>
    /// Optional per-task model overrides. Key is the <see cref="AiTask"/> name (e.g. "LeakExplanation").
    /// Falls back to <see cref="Model"/> when absent. Note: <see cref="WritingThinking"/> and
    /// <see cref="DebriefEffort"/> are model-specific; ensure they are compatible with any overridden model.
    /// </summary>
    public Dictionary<string, string> ModelOverrides { get; set; } = new();

    public Uri BaseUrl { get; set; } = new("https://api.anthropic.com/");

    public int MaxOutputTokens { get; set; } = 2_000;

    /// <summary>
    /// Optional per-task max output token overrides. Key is the <see cref="AiTask"/> name.
    /// Useful when a task model override (e.g. Haiku, which does not use thinking tokens) needs a
    /// larger visible output budget than the global <see cref="MaxOutputTokens"/>.
    /// </summary>
    public Dictionary<string, int> MaxOutputTokensOverrides { get; set; } = new();

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
    private const string MttContext = """
        You are the coach inside NutsIQ, an analysis tool for online multi-table tournament (MTT) players
        at low stakes (5 to 20 euros), mostly 6-max, with antes. Large fields (50 to 2000+ players):
        chip EV early, ICM pressure builds at the bubble and pay jumps.

        """;

    private const string RebuyMttContext = """
        You are the coach inside NutsIQ, an analysis tool for online rebuy MTT players at low stakes
        (5 to 20 euros), mostly 6-max, with antes. During the rebuy period chip EV logic applies
        (ICM is low); after the add-on the dynamics match a standard MTT.

        """;

    private const string SngContext = """
        You are the coach inside NutsIQ, an analysis tool for online sit-and-go (SNG) players at low
        stakes (2 to 10 euros), mostly 6-max or 9-max. Fixed small field (6 to 9 players), no ante,
        no late registration, no rebuy. ICM pressure starts from the first level and increases rapidly:
        the bubble arrives after just a few eliminations. The prize pool covers the top 2 or 3 spots.

        """;

    private const string BountySnippet = """
        This tournament has bounties (progressive knockout or mystery bounty). Bounty equity is immediate
        cash: it shifts ranges — wider calls against high-bounty opponents, wider shoves when your own
        bounty matters to theirs. Bounty EV is separate from chip EV and ICM equity.

        """;

    private const string CashContext = """
        You are the coach inside NutsIQ, an analysis tool for online cash game players at low
        stakes (0.01€/0.02€ to 0.25€/0.50€), mostly 6-max, no antes.

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

    private const string LeakRules = """
        You receive one leak: a statistic where the player differs from solid regulars, with figures that
        were computed by the software, and a few of the player's own hands where the leak shows.

        Rules:
        - Use only the figures given. Never compute, estimate or invent a statistic, a percentage or a range.
        - Talk about the hands only through the facts given (positions, stacks in big blinds, actions, board,
          the hero's cards). Opponents are known only by position. Do not guess cards that were not shown.
        - Reference hands only by their id (H1, H2...), at most three, choosing the most instructive ones.
        - Be concrete and practical, for a recreational player who wants to improve: no jargon without a
          short explanation, no moralising, no generic advice that would fit any leak.
        - If the sample is small ("possible"), say so plainly and frame the advice as something to check.
        """;

    private const string LeakFields = """
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

    private const string Instructions = MttContext + LeakRules + """
        - Tournament context matters: stack depth in big blinds, antes, bounties, pay jumps.
        """ + Voice + LeakFields;

    private const string CashInstructions = CashContext + LeakRules + """
        - Cash game context: effective stacks in big blinds (typically 50–100 BB), rake, no antes or bounties. BB/100 is the key profit metric.
        """ + Voice + LeakFields;

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

    private const string DebriefRules = """
        Rules:
        - Use only the figures given. Never compute, estimate or invent a statistic, a percentage, a finish
          or an amount of money. If the result is unknown, do not talk about the finish or the money.
        - Talk about the hands only through the facts given (positions, stacks in big blinds, actions, board,
          cards). Opponents are known only by position, and their cards only if they were shown.
        - Judge decisions, not results: a lost all-in can be well played, a won pot can be a mistake.
        - Verdict "variance" only for a preflop all-in whose computed equity is given and whose result went
          against it. When the facts are not enough to judge a decision, use "standard".
        - Be concrete and practical, for a recreational player who wants to improve: no jargon without a
          short explanation, no moralising, no generic advice that would fit any single session.
        """;

    private const string DebriefFields = """
        Fill the fields:
        - headline: one sentence that sums up the session.
        - story: three to five sentences on how the session went, from the stack and the key moments.
        - moments: the key moments worth a comment (at most six), each with its ref (M1, M2...), a verdict
          and a note of one or two sentences: what happened and what to do next time.
        - strengths: one to three things the player did well, tied to the facts given.
        - work_on: one to three concrete things to work on, tied to the facts given.
        """;

    private const string MttDebriefInstructions = MttContext + """
        You receive the facts of one tournament the player played, computed by the software, and the stories
        of its key moments: the hands that won or lost at least a quarter of the stack.

        """ + DebriefRules + """
        - One tournament is a small sample: its statistics describe this tournament, they never prove a leak.
          Link a moment to one of the player's long-term leaks only when the hand clearly shows it.
        - Tournament context matters: stack depth in big blinds, antes, pay jumps.
        """ + Voice + DebriefFields;

    private const string MttBountyDebriefInstructions = MttContext + BountySnippet + """
        You receive the facts of one tournament the player played, computed by the software, and the stories
        of its key moments: the hands that won or lost at least a quarter of the stack.

        """ + DebriefRules + """
        - One tournament is a small sample: its statistics describe this tournament, they never prove a leak.
          Link a moment to one of the player's long-term leaks only when the hand clearly shows it.
        - Tournament context matters: stack depth in big blinds, antes, bounties, pay jumps.
        """ + Voice + DebriefFields;

    private const string RebuyMttDebriefInstructions = RebuyMttContext + """
        You receive the facts of one tournament the player played, computed by the software, and the stories
        of its key moments: the hands that won or lost at least a quarter of the stack.

        """ + DebriefRules + """
        - One tournament is a small sample: its statistics describe this tournament, they never prove a leak.
          Link a moment to one of the player's long-term leaks only when the hand clearly shows it.
        - Tournament context matters: stack depth in big blinds, antes, rebuy period vs. post-add-on, pay jumps.
        """ + Voice + DebriefFields;

    private const string RebuyMttBountyDebriefInstructions = RebuyMttContext + BountySnippet + """
        You receive the facts of one tournament the player played, computed by the software, and the stories
        of its key moments: the hands that won or lost at least a quarter of the stack.

        """ + DebriefRules + """
        - One tournament is a small sample: its statistics describe this tournament, they never prove a leak.
          Link a moment to one of the player's long-term leaks only when the hand clearly shows it.
        - Tournament context matters: stack depth in big blinds, antes, bounties, rebuy period vs. post-add-on, pay jumps.
        """ + Voice + DebriefFields;

    private const string SngDebriefInstructions = SngContext + """
        You receive the facts of one sit-and-go the player played, computed by the software, and the stories
        of its key moments: the hands that won or lost at least a quarter of the stack.

        """ + DebriefRules + """
        - One SNG is a small sample: its statistics describe this SNG, they never prove a leak.
          Link a moment to one of the player's long-term leaks only when the hand clearly shows it.
        - SNG context matters: short field means ICM pressure from the start, no antes, fast blinds,
          short-stack shove/fold decisions dominate the middle and late game.
        """ + Voice + DebriefFields;

    private const string SngBountyDebriefInstructions = SngContext + BountySnippet + """
        You receive the facts of one sit-and-go the player played, computed by the software, and the stories
        of its key moments: the hands that won or lost at least a quarter of the stack.

        """ + DebriefRules + """
        - One SNG is a small sample: its statistics describe this SNG, they never prove a leak.
          Link a moment to one of the player's long-term leaks only when the hand clearly shows it.
        - SNG context matters: short field means ICM pressure from the start, no antes, fast blinds,
          short-stack shove/fold decisions dominate the middle and late game. Bounty equity amplifies
          the value of eliminations.
        """ + Voice + DebriefFields;

    private static string TournamentDebriefInstructions(TournamentSubFormat subFormat, bool isBounty) =>
        (subFormat, isBounty) switch
        {
            (TournamentSubFormat.Sng, false) => SngDebriefInstructions,
            (TournamentSubFormat.Sng, true) => SngBountyDebriefInstructions,
            (TournamentSubFormat.RebuyMtt, false) => RebuyMttDebriefInstructions,
            (TournamentSubFormat.RebuyMtt, true) => RebuyMttBountyDebriefInstructions,
            (_, true) => MttBountyDebriefInstructions,
            _ => MttDebriefInstructions,
        };

    private const string CashDebriefInstructions = CashContext + """
        You receive the facts of one cash game session the player played, computed by the software, and the
        stories of its key moments: the hands with the largest absolute result in big blinds.

        """ + DebriefRules + """
        - One session is a small sample: its statistics describe this session, they never prove a leak.
          Link a moment to one of the player's long-term leaks only when the hand clearly shows it.
        - Cash game context: effective stacks in big blinds, rake, no antes, no bounties, no pay jumps.
          There is no buy-in, no prize pool, no finish position. BB/100 is the performance metric.
        """ + Voice + DebriefFields;

    private const string ReviewRules = """
        Rules:
        - Use only the figures given. Never compute, estimate or invent a statistic or a percentage.
        - The status given for each priority is the measurement: do not contradict it. "Not judged" means too
          few spots: encourage playing or drilling that spot, draw no conclusion.
        - Results over a week are mostly variance: never judge the player's level or the plan from them.
        - If the week is in progress, write a mid-week check and what to do in the days left.
        - Be concrete and encouraging without flattery: name what improved, what did not, and the next step.
        """;

    private const string ReviewFields = """
        Fill the fields:
        - headline: one sentence on the week.
        - summary: two to four sentences on the week against the plan.
        - priorities: for each priority (ref P1, P2...), one or two sentences: what its figure says and the
          next concrete step (a drill, a spot to watch at the table).
        - next_steps: two or three concrete actions for the coming days.
        """;

    private const string ReviewInstructions = MttContext + """
        You receive the player's week: the plan's priorities (leaks chosen on Monday), how each one moved on
        the week's hands, the drills done and the results, all computed by the software.

        """ + ReviewRules + Voice + ReviewFields;

    private const string RebuyMttReviewInstructions = RebuyMttContext + """
        You receive the player's week: the plan's priorities (leaks chosen on Monday), how each one moved on
        the week's hands, the drills done and the results, all computed by the software.

        """ + ReviewRules + Voice + ReviewFields;

    private const string SngReviewInstructions = SngContext + """
        You receive the player's week: the plan's priorities (leaks chosen on Monday), how each one moved on
        the week's hands, the drills done and the results, all computed by the software.

        """ + ReviewRules + Voice + ReviewFields;

    private const string CashReviewInstructions = CashContext + """
        You receive the player's week: the plan's priorities (leaks chosen on Monday), how each one moved on
        the week's hands, the drills done and the results, all computed by the software.

        """ + ReviewRules + Voice + ReviewFields;

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

    private const string BankrollCoachInstructions = """
        You are the bankroll management coach inside NutsIQ, an analysis tool for online tournament
        poker players at low stakes (5 to 20 euros).

        You receive the player's bankroll figures, computed by the software.

        Rules:
        - Use only the figures given. Never compute, estimate or invent a statistic or a percentage.
        - verdict "not_enough_data": fewer than 50 tournaments in the performance sample.
        - verdict "move_down": recent average buy-in is above the max allowed, OR risk of ruin is above 10%.
        - verdict "move_up": recent average buy-in is ≤ 70% of the max allowed, AND the ROI confidence interval lower bound is positive.
        - verdict "stay" in every other case.
        - headline: one concrete sentence (≤ 15 words) — what to do and the one key number that drives it.
        - reasoning: 2–3 sentences — which specific numbers led to the verdict, what they mean, and the one metric to watch.
        - Be direct and practical, for a recreational player who wants to improve.
        - Do not suggest a specific stake amount (you do not know which stakes are available).

        """ + Voice;

    private static readonly JsonObject BankrollSchema = new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("verdict", "headline", "reasoning"),
        ["properties"] = new JsonObject
        {
            ["verdict"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("stay", "move_up", "move_down", "not_enough_data") },
            ["headline"] = Text.DeepClone(),
            ["reasoning"] = Text.DeepClone(),
        },
    };

    private const string MonthlyReportRules = """
        Rules:
        - Use only the figures given. Never compute, estimate or invent a statistic or a percentage.
        - A month of results is mostly variance: never judge the player's level from the money alone.
        - Focus on leak trends (more or fewer leaks than last month) and training consistency.
        - Be concrete and encouraging without flattery: name what improved, what did not, and the recommendation.
        """;

    private const string MonthlyReportFields = """
        Fill the fields:
        - headline: one sentence on the month.
        - summary: three to five sentences on the month's performance, leaks and training.
        - progress: two or three concrete things the player did well or improved.
        - weak_points: two or three things that remain fragile or got worse.
        - next_month: two or three concrete recommendations for the next month.
        """;

    private const string MonthlyReportInstructions = MttContext + """
        You receive the player's monthly summary: tournament and cash results, leaks compared to the
        previous month, and training activity, all computed by the software.

        """ + MonthlyReportRules + Voice + MonthlyReportFields;

    private static readonly JsonObject MonthlyReportSchema = new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("headline", "summary", "progress", "weak_points", "next_month"),
        ["properties"] = new JsonObject
        {
            ["headline"] = Text.DeepClone(),
            ["summary"] = Text.DeepClone(),
            ["progress"] = Texts.DeepClone(),
            ["weak_points"] = Texts.DeepClone(),
            ["next_month"] = Texts.DeepClone(),
        },
    };

    private const int MonthlyReportMaxOutputTokens = 2_500;

    private const string TournamentSelectionInstructions = MttContext + """
        You receive the player's performance figures broken down by buy-in band, tournament type and
        speed, all computed by the software over the player's entire history.

        Rules:
        - Use only the figures given. Never compute, estimate or invent a statistic or a percentage.
        - Groups with fewer than 20 tournaments are unreliable: mention the uncertainty explicitly.
        - Focus on where the player is most profitable (positive ROI, good ITM) and where they lose.
        - Be concrete: name the buy-in bands, types and speeds.
        - Do not suggest a specific stake amount or room.

        Fill the fields:
        - headline: one sentence on the overall recommendation.
        - summary: three to five sentences analysing the player's strengths and weaknesses by tournament type.
        - strengths: two or three tournament categories where the player performs best, with the key figure.
        - avoid: one or two tournament categories the player should reduce or avoid, with the key figure.
        - recommendations: two or three concrete, actionable suggestions for tournament selection.

        """ + Voice;

    private static readonly JsonObject TournamentSelectionSchema = new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("headline", "summary", "strengths", "avoid", "recommendations"),
        ["properties"] = new JsonObject
        {
            ["headline"] = Text.DeepClone(),
            ["summary"] = Text.DeepClone(),
            ["strengths"] = Texts.DeepClone(),
            ["avoid"] = Texts.DeepClone(),
            ["recommendations"] = Texts.DeepClone(),
        },
    };

    private const int TournamentSelectionMaxOutputTokens = 1_500;

    private const string StrategyPlanRules = """

        Rules:
        - Use only the figures given. Never compute, estimate or invent a statistic, a percentage or a range.
        - Be concrete: name the bands or stakes, the leaks by their stat name, and the specific adjustments.
        - The approach is "gto" (GTO-first: balanced ranges, standard sizings, minimal reads-based
          adjustments), "exploitative" (reads-first: wide deviations from balance targeting field
          tendencies), or "mixed" (GTO baseline with specific exploitative adjustments at identified spots).
        - Do not suggest a specific stake amount, room, or solver.
        - If the player has few entries in a band, say so and frame the recommendation as tentative.
        - When a confirmed leak directly impacts a band's performance, connect them explicitly.

        Fill the fields:
        - headline: one sentence on the overall strategic recommendation.
        - overview: three to five sentences on the player's strategic profile across levels,
          linking performance and leaks.
        - bands: one entry per band or stake level the player has played, each with:
          - band: the band or stake key exactly as given in the facts.
          - approach: "gto", "exploitative", or "mixed".
          - rationale: two or three sentences on why this approach suits this band for this player.
          - adjustments: two or three concrete preflop or postflop adjustments for this band,
            tied to the player's leaks or the field's tendencies.
          - priority_leak: the one confirmed or possible leak that costs the most at this stake,
            with one sentence on why. Empty string if no leak is relevant or data is insufficient.
        - progression: two or three sentences on the recommended path for moving between levels
          (where to focus volume, when to consider moving up or down).
        - key_focus: two or three things to work on across all levels, each one sentence.

        """;

    private const string TournamentStrategyPlanInstructions = MttContext + """
        You receive the player's performance figures broken down by buy-in band, tournament type and
        speed, and their confirmed and possible leaks, all computed by the software over the player's
        entire history.

        Your task is to build a strategic plan for each buy-in band the player has played, recommending
        whether a GTO-based, exploitative, or mixed approach is most profitable at that stake level
        given the player's profile.

        Framework for the approach recommendation:
        - At micro stakes (up to 5 €), fields are mostly recreational players. An exploitative approach
          that targets common mistakes (overcalling, passive play, poor ICM awareness) is usually more
          profitable than a balanced one.
        - At low stakes (5 to 20 €), fields mix recreational and regular players. A solid default
          strategy with targeted exploitative adjustments against weaker opponents is the sweet spot.
        - At mid stakes (20 € and above), fields are mostly regulars. A GTO baseline becomes necessary
          to avoid being exploited; deviations require solid reads.
        - The approach must account for the player's own leaks: a player with confirmed leaks should
          fix fundamentals before attempting advanced exploits.
        - Groups with fewer than 20 tournaments are unreliable: mention the uncertainty explicitly.

        """ + StrategyPlanRules + Voice;

    private const string CashStrategyPlanInstructions = CashContext + """
        You receive the player's performance figures broken down by stake level, and their confirmed
        and possible leaks, all computed by the software over the player's entire history.

        Your task is to build a strategic plan for each stake level the player has played, recommending
        whether a GTO-based, exploitative, or mixed approach is most profitable at that level
        given the player's profile.

        Framework for the approach recommendation:
        - At micro stakes (NL2 to NL5), fields are mostly recreational players. An exploitative approach
          that targets common mistakes (overcalling, passive postflop play, poor positional awareness)
          is usually more profitable than a balanced one. Rake is high relative to the pot.
        - At low stakes (NL10 to NL25), fields mix recreational and regular players. A solid default
          strategy with targeted exploitative adjustments against weaker opponents is the sweet spot.
        - At mid stakes (NL50 and above), fields are mostly regulars. A GTO baseline becomes necessary
          to avoid being exploited; deviations require solid reads. Rake impact decreases.
        - The approach must account for the player's own leaks: a player with confirmed leaks should
          fix fundamentals before attempting advanced exploits.
        - Groups with fewer than 500 hands are unreliable: mention the uncertainty explicitly.

        """ + StrategyPlanRules + Voice;

    private static readonly JsonObject StrategyPlanBandSchema = new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("band", "approach", "rationale", "adjustments", "priority_leak"),
        ["properties"] = new JsonObject
        {
            ["band"] = Text.DeepClone(),
            ["approach"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("gto", "exploitative", "mixed") },
            ["rationale"] = Text.DeepClone(),
            ["adjustments"] = Texts.DeepClone(),
            ["priority_leak"] = Text.DeepClone(),
        },
    };

    private static readonly JsonObject StrategyPlanSchema = new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("headline", "overview", "bands", "progression", "key_focus"),
        ["properties"] = new JsonObject
        {
            ["headline"] = Text.DeepClone(),
            ["overview"] = Text.DeepClone(),
            ["bands"] = new JsonObject { ["type"] = "array", ["items"] = StrategyPlanBandSchema.DeepClone() },
            ["progression"] = Text.DeepClone(),
            ["key_focus"] = Texts.DeepClone(),
        },
    };

    private const int StrategyPlanMaxOutputTokens = 2_500;

    private const string PlayerProfileBody = """
        You receive a player's overall statistics and the reference ranges used by the software for
        a solid regular at these stakes. The reference ranges are the source of truth for what
        "balanced" or "in range" means — use them, not your own thresholds.

        Your task is to classify the player into exactly one archetype among: nit, tag, lag,
        calling_station, maniac, shark. Choose the closest match.

        Archetype definitions (compare the player's stats to the reference ranges given):
        - nit: VPIP well below the reference range low end, very few hands played, ultra-tight.
        - tag (tight-aggressive): VPIP in or slightly below the reference range, PFR close to VPIP
          (gap within the VPIP-PFR gap range), 3-bet within range. Selective but aggressive.
        - lag (loose-aggressive): VPIP above the reference range high end, PFR still close to VPIP,
          3-bet above range, high postflop aggression above range.
        - calling_station: VPIP above the reference range, large VPIP–PFR gap (well above the gap
          range), postflop aggression below range, limp rate above range.
        - maniac: VPIP far above the reference range, very high PFR, aggression far above range.
        - shark: most statistics within the reference ranges, solid postflop play, limp within range.

        Rules:
        - Use only the figures given. Never compute, estimate or invent a statistic or a percentage.
        - Compare the player's stats to the reference ranges provided in the data. Cite the
          reference range when explaining why a stat is too high, too low, or balanced.
        - title: a fun, memorable title for the archetype (2–5 words), in the requested language.
        - description: two or three sentences describing what this profile means for the player, with the
          key stats that drove the classification and how they compare to the reference ranges.
        - strengths: two or three things the player does well, tied to their stats.
        - weaknesses: two or three things the player should improve, tied to their stats.
        - advice: two or three sentences of concrete, actionable advice for this archetype to move toward
          a more balanced, winning style.

        """;

    private const string PlayerProfileInstructions = MttContext + PlayerProfileBody + Voice;

    private const string CashPlayerProfileInstructions = CashContext + PlayerProfileBody + Voice;

    private static readonly JsonObject PlayerProfileSchema = new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("archetype", "title", "description", "strengths", "weaknesses", "advice"),
        ["properties"] = new JsonObject
        {
            ["archetype"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("nit", "tag", "lag", "calling_station", "maniac", "shark") },
            ["title"] = Text.DeepClone(),
            ["description"] = Text.DeepClone(),
            ["strengths"] = Texts.DeepClone(),
            ["weaknesses"] = Texts.DeepClone(),
            ["advice"] = Text.DeepClone(),
        },
    };

    private const int PlayerProfileMaxOutputTokens = 1_000;

    private const string HandCommentaryInstructions = MttContext + """
        You receive one or more hands from a tournament the player played, told as short anonymous stories
        by the software. Your task is to comment each hand: what happened, whether the hero played well,
        and what to do differently if applicable.

        Rules:
        - Use only the facts given. Never compute, estimate or invent a statistic, a percentage or a card.
        - Opponents are known only by position. Do not guess cards that were not shown.
        - Judge decisions, not results: a lost all-in can be well played, a won pot can be a mistake.
        - verdict "well_played": the hero made the right decision given the information available.
        - verdict "mistake": a clear error that costs chips in the long run.
        - verdict "variance": a preflop all-in whose equity is given and whose result went against it.
        - verdict "standard": a normal play either way, or not enough information to judge.
        - verdict "routine": the hand has no notable decision (e.g. fold preflop, walk in the big blind).
        - analysis: two to four sentences describing the key decision(s) street by street.
        - suggestion: one sentence on what to do differently; null when well played, routine or variance.
        - Be concrete and practical, for a recreational player who wants to improve.
        """ + Voice;

    private static readonly JsonObject HandCommentaryNoteSchema = new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("ref", "verdict", "analysis"),
        ["properties"] = new JsonObject
        {
            ["ref"] = Text.DeepClone(),
            ["verdict"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("well_played", "mistake", "variance", "standard", "routine") },
            ["analysis"] = Text.DeepClone(),
            ["suggestion"] = new JsonObject { ["type"] = "string" },
        },
    };

    private static readonly JsonObject HandCommentarySchema = new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("hands"),
        ["properties"] = new JsonObject
        {
            ["hands"] = new JsonObject { ["type"] = "array", ["items"] = HandCommentaryNoteSchema.DeepClone() },
        },
    };

    private const int HandCommentaryMaxOutputTokens = 4_000;

    private const string OpponentCoachingInstructions = MttContext + """
        You receive an opponent's aggregate statistics over multiple hands, their deterministic archetype
        classification, and the generic exploit tips the software already provides for that archetype.

        Your task is to write a precise, personalised scouting report that goes beyond the generic tips:
        reference the opponent's actual numbers (e.g. "Fold to 3-bet is 72 %, well above average") and
        give concrete lines and ranges, not just general directions. If the sample is small (under 50
        hands), say so and frame the advice as tendencies to watch.

        Rules:
        - Use only the figures given. Never compute, estimate or invent a statistic or a percentage.
        - The opponent is anonymous: refer to them as "this opponent" or "they".
        - Be concrete and practical, for a recreational player who wants to improve. Give the advice as
          if briefing the hero before a session where they will face this opponent again.
        - summary: two or three sentences describing who this opponent is and their key tendencies.
        - adjustments: three to five concrete adjustments, each with:
          - spot: one sentence describing the specific game situation.
          - action: one sentence on the concrete action to take against this opponent.
          - why: one sentence explaining why it exploits their tendency, citing their specific stat.
        - key_tips: two or three one-line reminders the hero can paste into their HUD note or keep in mind.
        """ + Voice;

    private static readonly JsonObject OpponentAdjustmentSchema = new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("spot", "action", "why"),
        ["properties"] = new JsonObject
        {
            ["spot"] = new JsonObject { ["type"] = "string" },
            ["action"] = new JsonObject { ["type"] = "string" },
            ["why"] = new JsonObject { ["type"] = "string" },
        },
    };

    private static readonly JsonObject OpponentCoachingSchema = new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("summary", "adjustments", "key_tips"),
        ["properties"] = new JsonObject
        {
            ["summary"] = Text.DeepClone(),
            ["adjustments"] = new JsonObject { ["type"] = "array", ["items"] = OpponentAdjustmentSchema.DeepClone() },
            ["key_tips"] = Texts.DeepClone(),
        },
    };

    private const int OpponentCoachingMaxOutputTokens = 1_500;

    private const int MaxCorrectionAttempts = 1;

    /// <summary>Thinking and answer share this cap: room for the reasoning on eight hands plus the text.</summary>
    private const int DebriefMaxOutputTokens = 12_000;

    private const int ReviewMaxOutputTokens = 2_000;

    private const int BankrollCoachMaxOutputTokens = 800;

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

    private string ModelFor(AiTask task)
    {
        var settings = options.Value;
        return settings.ModelOverrides.TryGetValue(task.ToString(), out var m) ? m : settings.Model;
    }

    private int MaxOutputTokensFor(AiTask task)
    {
        var settings = options.Value;
        if (settings.MaxOutputTokensOverrides.TryGetValue(task.ToString(), out var o))
            return o;
        return task switch
        {
            AiTask.TournamentDebrief or AiTask.CashDebrief => DebriefMaxOutputTokens,
            AiTask.WeekReview => ReviewMaxOutputTokens,
            AiTask.MonthlyReport => MonthlyReportMaxOutputTokens,
            AiTask.BankrollCoach => BankrollCoachMaxOutputTokens,
            AiTask.TournamentSelection => TournamentSelectionMaxOutputTokens,
            AiTask.StrategyPlan => StrategyPlanMaxOutputTokens,
            AiTask.PlayerProfile => PlayerProfileMaxOutputTokens,
            AiTask.HandCommentary => HandCommentaryMaxOutputTokens,
            AiTask.OpponentCoaching => OpponentCoachingMaxOutputTokens,
            _ => settings.MaxOutputTokens,
        };
    }

    /// <summary>
    /// Every attempt at full output, and every input at the cache-write rate (the dearest): a correction resends the
    /// prompt plus the rejected answer.
    /// </summary>
    public ModelUsage Ceiling(AiTask task)
    {
        var settings = options.Value;
        var output = MaxOutputTokensFor(task);
        const int attempts = 1 + MaxCorrectionAttempts;
        return new ModelUsage(
            ModelFor(task),
            InputTokens: 0,
            OutputTokens: attempts * output,
            CacheWriteTokens: (attempts * settings.CeilingInputTokens) + (MaxCorrectionAttempts * output),
            CacheReadTokens: 0);
    }

    public Task<ModelAnswer<LeakExplanation>> ExplainAsync(ExplanationPrompt prompt, CancellationToken cancellationToken) =>
        LoggedAsync(
            prompt.GameType == GameType.CashGame ? "cash-leak-explanation" : "leak-explanation",
            () => ExplainCoreAsync(prompt, ModelFor(AiTask.LeakExplanation), cancellationToken));

    public Task<ModelAnswer<TournamentDebrief>> DebriefTournamentAsync(DebriefPrompt prompt, CancellationToken cancellationToken) =>
        LoggedAsync("tournament-debrief", () => DebriefCoreAsync(TournamentDebriefInstructions(prompt.SubFormat, prompt.IsBounty), prompt, AiTask.TournamentDebrief, cancellationToken));

    public Task<ModelAnswer<TournamentDebrief>> DebriefCashSessionAsync(DebriefPrompt prompt, CancellationToken cancellationToken) =>
        LoggedAsync("cash-debrief", () => DebriefCoreAsync(CashDebriefInstructions, prompt, AiTask.CashDebrief, cancellationToken));

    public Task<ModelAnswer<WeekReview>> ReviewWeekAsync(WeekReviewPrompt prompt, CancellationToken cancellationToken) =>
        LoggedAsync("week-review", () => ReviewCoreAsync(prompt, ModelFor(AiTask.WeekReview), cancellationToken));

    public Task<ModelAnswer<BankrollAdvice>> AdviseOnBankrollAsync(BankrollCoachPrompt prompt, CancellationToken cancellationToken) =>
        LoggedAsync("bankroll-coach", () => BankrollAdviceCoreAsync(prompt, ModelFor(AiTask.BankrollCoach), cancellationToken));

    public Task<ModelAnswer<MonthlyReport>> ReviewMonthAsync(MonthlyReportPrompt prompt, CancellationToken cancellationToken) =>
        LoggedAsync("monthly-report", () => MonthlyReportCoreAsync(prompt, ModelFor(AiTask.MonthlyReport), cancellationToken));

    public Task<ModelAnswer<TournamentSelectionAdvice>> AdviseTournamentSelectionAsync(TournamentSelectionPrompt prompt, CancellationToken cancellationToken) =>
        LoggedAsync("tournament-selection", () => TournamentSelectionCoreAsync(prompt, ModelFor(AiTask.TournamentSelection), cancellationToken));

    public Task<ModelAnswer<StrategyPlanAdvice>> AdviseStrategyPlanAsync(StrategyPlanPrompt prompt, CancellationToken cancellationToken) =>
        LoggedAsync(
            prompt.GameType == GameType.CashGame ? "cash-strategy-plan" : "strategy-plan",
            () => StrategyPlanCoreAsync(prompt, ModelFor(AiTask.StrategyPlan), cancellationToken));

    public Task<ModelAnswer<PlayerProfile>> ProfilePlayerAsync(PlayerProfilePrompt prompt, CancellationToken cancellationToken) =>
        LoggedAsync(
            prompt.GameType == GameType.CashGame ? "cash-player-profile" : "player-profile",
            () => PlayerProfileCoreAsync(prompt, ModelFor(AiTask.PlayerProfile), cancellationToken));

    public Task<ModelAnswer<HandCommentaryBatch>> CommentHandsAsync(HandCommentaryPrompt prompt, CancellationToken cancellationToken) =>
        LoggedAsync("hand-commentary", () => HandCommentaryCoreAsync(prompt, ModelFor(AiTask.HandCommentary), cancellationToken));

    public Task<ModelAnswer<OpponentCoachingAdvice>> CoachAgainstOpponentAsync(OpponentCoachingPrompt prompt, CancellationToken cancellationToken) =>
        LoggedAsync("opponent-coaching", () => OpponentCoachingCoreAsync(prompt, ModelFor(AiTask.OpponentCoaching), cancellationToken));

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

    private async Task<ModelAnswer<LeakExplanation>> ExplainCoreAsync(ExplanationPrompt prompt, string model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var instructions = prompt.GameType == GameType.CashGame ? CashInstructions : Instructions;
        var user = $"Answer language: {prompt.Language}.\n\nLeak:\n{prompt.Facts}\n\n"
            + string.Join("\n\n", prompt.Hands.Select(h => $"Hand {h.Ref}:\n{h.Story}"));
        var (answer, usage) = await CallAsync<Answer>(instructions, Schema, user, MaxOutputTokensFor(AiTask.LeakExplanation), Reasoning.Writing, cancellationToken, model: model);
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

    private async Task<ModelAnswer<TournamentDebrief>> DebriefCoreAsync(string instructions, DebriefPrompt prompt, AiTask task, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var user = $"Answer language: {prompt.Language}.\n\n{prompt.Facts}\n\n"
            + string.Join("\n\n", prompt.Moments.Select(m => $"Key moment {m.Ref}:\n{m.Story}"));
        var (answer, usage) = await CallAsync<DebriefAnswer>(instructions, DebriefSchema, user, MaxOutputTokensFor(task), Reasoning.Judging, cancellationToken, model: ModelFor(task));
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

    private async Task<ModelAnswer<WeekReview>> ReviewCoreAsync(WeekReviewPrompt prompt, string model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var instructions = prompt.GameType == GameType.CashGame
            ? CashReviewInstructions
            : prompt.SubFormat switch
            {
                TournamentSubFormat.Sng => SngReviewInstructions,
                TournamentSubFormat.RebuyMtt => RebuyMttReviewInstructions,
                _ => ReviewInstructions,
            };
        var user = $"Answer language: {prompt.Language}.\n\n{prompt.Facts}";
        var (answer, usage) = await CallAsync<ReviewAnswer>(instructions, ReviewSchema, user, MaxOutputTokensFor(AiTask.WeekReview), Reasoning.Writing, cancellationToken, model: model);
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

    private async Task<ModelAnswer<BankrollAdvice>> BankrollAdviceCoreAsync(BankrollCoachPrompt prompt, string model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var user = $"Answer language: {prompt.Language}.\n\n{prompt.Facts}";
        var (answer, usage) = await CallAsync<BankrollAdviceAnswer>(BankrollCoachInstructions, BankrollSchema, user, MaxOutputTokensFor(AiTask.BankrollCoach), Reasoning.Writing, cancellationToken, model: model);
        if (string.IsNullOrWhiteSpace(answer.Headline) || string.IsNullOrWhiteSpace(answer.Reasoning))
        {
            throw new CoachingModelException("The model answer misses required fields.", usage);
        }

        var verdict = answer.Verdict switch
        {
            "move_up" => BankrollCoachVerdict.MoveUp,
            "move_down" => BankrollCoachVerdict.MoveDown,
            "not_enough_data" => BankrollCoachVerdict.NotEnoughData,
            _ => BankrollCoachVerdict.Stay,
        };
        return new ModelAnswer<BankrollAdvice>(new BankrollAdvice(verdict, answer.Headline.Trim(), answer.Reasoning.Trim()), usage);
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
        int correctionAttempts = 0,
        string? model = null)
        where T : class
    {
        var settings = options.Value;
        var effectiveModel = model ?? settings.Model;
        var body = new JsonObject
        {
            ["model"] = effectiveModel,
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
        var supportsThinking = !effectiveModel.Contains("haiku", StringComparison.OrdinalIgnoreCase);
        if (supportsThinking && reasoning == Reasoning.Writing && settings.WritingThinking is { Length: > 0 } thinking)
        {
            body["thinking"] = new JsonObject { ["type"] = thinking };
        }
        else if (supportsThinking && reasoning == Reasoning.Judging && settings.DebriefEffort is { Length: > 0 } effort)
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
                throw new CoachingModelException($"The model provider answered {(int)httpResponse.StatusCode}.", new ModelUsage(effectiveModel, 0, 0, 0, 0));
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
            response.Model ?? effectiveModel,
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
                    correctionAttempts + 1,
                    model);
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

    private sealed record BankrollAdviceAnswer(string? Verdict, string? Headline, string? Reasoning);

    private sealed record MonthlyReportAnswer(string Headline, string Summary, IReadOnlyList<string>? Progress, IReadOnlyList<string>? WeakPoints, IReadOnlyList<string>? NextMonth);

    private async Task<ModelAnswer<MonthlyReport>> MonthlyReportCoreAsync(MonthlyReportPrompt prompt, string model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var user = $"Answer language: {prompt.Language}.\n\n{prompt.Facts}";
        var (answer, usage) = await CallAsync<MonthlyReportAnswer>(MonthlyReportInstructions, MonthlyReportSchema, user, MaxOutputTokensFor(AiTask.MonthlyReport), Reasoning.Writing, cancellationToken, model: model);
        if (string.IsNullOrWhiteSpace(answer.Headline) || string.IsNullOrWhiteSpace(answer.Summary))
        {
            throw new CoachingModelException("The model answer misses required fields.", usage);
        }

        return new ModelAnswer<MonthlyReport>(
            new MonthlyReport(
                answer.Headline.Trim(),
                answer.Summary.Trim(),
                Clean(answer.Progress, 3),
                Clean(answer.WeakPoints, 3),
                Clean(answer.NextMonth, 3)),
            usage);
    }

    private sealed record TournamentSelectionAnswer(string? Headline, string? Summary, IReadOnlyList<string>? Strengths, IReadOnlyList<string>? Avoid, IReadOnlyList<string>? Recommendations);

    private async Task<ModelAnswer<TournamentSelectionAdvice>> TournamentSelectionCoreAsync(TournamentSelectionPrompt prompt, string model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var user = $"Answer language: {prompt.Language}.\n\n{prompt.Facts}";
        var (answer, usage) = await CallAsync<TournamentSelectionAnswer>(TournamentSelectionInstructions, TournamentSelectionSchema, user, MaxOutputTokensFor(AiTask.TournamentSelection), Reasoning.Writing, cancellationToken, model: model);
        if (string.IsNullOrWhiteSpace(answer.Headline) || string.IsNullOrWhiteSpace(answer.Summary))
        {
            throw new CoachingModelException("The model answer misses required fields.", usage);
        }

        return new ModelAnswer<TournamentSelectionAdvice>(
            new TournamentSelectionAdvice(
                answer.Headline.Trim(),
                answer.Summary.Trim(),
                Clean(answer.Strengths, 3),
                Clean(answer.Avoid, 2),
                Clean(answer.Recommendations, 3)),
            usage);
    }

    private sealed record StrategyPlanBandAnswer(string? Band, string? Approach, string? Rationale, IReadOnlyList<string>? Adjustments, string? PriorityLeak);

    private sealed record StrategyPlanAnswer(string? Headline, string? Overview, IReadOnlyList<StrategyPlanBandAnswer>? Bands, string? Progression, IReadOnlyList<string>? KeyFocus);

    private async Task<ModelAnswer<StrategyPlanAdvice>> StrategyPlanCoreAsync(StrategyPlanPrompt prompt, string model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var instructions = prompt.GameType == GameType.CashGame ? CashStrategyPlanInstructions : TournamentStrategyPlanInstructions;
        var user = $"Answer language: {prompt.Language}.\n\n{prompt.Facts}";
        var (answer, usage) = await CallAsync<StrategyPlanAnswer>(instructions, StrategyPlanSchema, user, MaxOutputTokensFor(AiTask.StrategyPlan), Reasoning.Writing, cancellationToken, model: model);
        if (string.IsNullOrWhiteSpace(answer.Headline) || string.IsNullOrWhiteSpace(answer.Overview))
        {
            throw new CoachingModelException("The model answer misses required fields.", usage);
        }

        var bands = (answer.Bands ?? [])
            .Where(b => !string.IsNullOrWhiteSpace(b.Band) && !string.IsNullOrWhiteSpace(b.Approach) && !string.IsNullOrWhiteSpace(b.Rationale))
            .Select(b => new StrategyPlanBand(
                b.Band!.Trim(),
                b.Approach!.Trim(),
                b.Rationale!.Trim(),
                Clean(b.Adjustments, 3),
                string.IsNullOrWhiteSpace(b.PriorityLeak) ? null : b.PriorityLeak!.Trim()))
            .ToList();

        return new ModelAnswer<StrategyPlanAdvice>(
            new StrategyPlanAdvice(
                answer.Headline.Trim(),
                answer.Overview.Trim(),
                bands,
                answer.Progression?.Trim() ?? string.Empty,
                Clean(answer.KeyFocus, 3)),
            usage);
    }

    private sealed record PlayerProfileAnswer(string? Archetype, string? Title, string? Description, IReadOnlyList<string>? Strengths, IReadOnlyList<string>? Weaknesses, string? Advice);

    private sealed record HandCommentaryAnswer(IReadOnlyList<HandCommentaryNoteAnswer>? Hands);

    private sealed record HandCommentaryNoteAnswer(string? Ref, string? Verdict, string? Analysis, string? Suggestion);

    private async Task<ModelAnswer<PlayerProfile>> PlayerProfileCoreAsync(PlayerProfilePrompt prompt, string model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var instructions = prompt.GameType == GameType.CashGame ? CashPlayerProfileInstructions : PlayerProfileInstructions;
        var user = $"Answer language: {prompt.Language}.\n\n{prompt.Facts}";
        var (answer, usage) = await CallAsync<PlayerProfileAnswer>(instructions, PlayerProfileSchema, user, MaxOutputTokensFor(AiTask.PlayerProfile), Reasoning.Writing, cancellationToken, model: model);
        if (string.IsNullOrWhiteSpace(answer.Archetype) || string.IsNullOrWhiteSpace(answer.Title) || string.IsNullOrWhiteSpace(answer.Description))
        {
            throw new CoachingModelException("The model answer misses required fields.", usage);
        }

        return new ModelAnswer<PlayerProfile>(
            new PlayerProfile(
                answer.Archetype.Trim(),
                answer.Title.Trim(),
                answer.Description.Trim(),
                Clean(answer.Strengths, 3),
                Clean(answer.Weaknesses, 3),
                answer.Advice?.Trim() ?? string.Empty),
            usage);
    }

    private static readonly HashSet<string> ValidCommentaryVerdicts = new(StringComparer.Ordinal)
        { "well_played", "mistake", "variance", "standard", "routine" };

    private async Task<ModelAnswer<HandCommentaryBatch>> HandCommentaryCoreAsync(HandCommentaryPrompt prompt, string model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var user = $"Answer language: {prompt.Language}.\n\n{prompt.TournamentFacts}\n\n"
            + string.Join("\n\n", prompt.Hands.Select(h => $"Hand {h.Ref}:\n{h.Story}"));
        var (answer, usage) = await CallAsync<HandCommentaryAnswer>(
            HandCommentaryInstructions, HandCommentarySchema, user,
            MaxOutputTokensFor(AiTask.HandCommentary), Reasoning.Judging, cancellationToken, model: model);

        var notes = (answer.Hands ?? [])
            .Where(h => h.Ref is not null && !string.IsNullOrWhiteSpace(h.Analysis) && ValidCommentaryVerdicts.Contains(h.Verdict ?? string.Empty))
            .Select(h => new HandCommentaryNote(
                h.Ref!,
                h.Verdict!,
                h.Analysis!.Trim(),
                string.IsNullOrWhiteSpace(h.Suggestion) ? null : h.Suggestion!.Trim()))
            .ToList();

        return new ModelAnswer<HandCommentaryBatch>(new HandCommentaryBatch(notes), usage);
    }

    private sealed record OpponentAdjustmentAnswer(string? Spot, string? Action, string? Why);

    private sealed record OpponentCoachingAnswer(string? Summary, IReadOnlyList<OpponentAdjustmentAnswer>? Adjustments, IReadOnlyList<string>? KeyTips);

    private async Task<ModelAnswer<OpponentCoachingAdvice>> OpponentCoachingCoreAsync(OpponentCoachingPrompt prompt, string model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var user = $"Answer language: {prompt.Language}.\n\n{prompt.Facts}";
        var (answer, usage) = await CallAsync<OpponentCoachingAnswer>(
            OpponentCoachingInstructions, OpponentCoachingSchema, user,
            MaxOutputTokensFor(AiTask.OpponentCoaching), Reasoning.Writing, cancellationToken, model: model);
        if (string.IsNullOrWhiteSpace(answer.Summary))
        {
            throw new CoachingModelException("The model answer misses required fields.", usage);
        }

        var adjustments = (answer.Adjustments ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a.Spot) && !string.IsNullOrWhiteSpace(a.Action) && !string.IsNullOrWhiteSpace(a.Why))
            .Select(a => new OpponentAdjustment(a.Spot!.Trim(), a.Action!.Trim(), a.Why!.Trim()))
            .Take(5)
            .ToList();

        return new ModelAnswer<OpponentCoachingAdvice>(
            new OpponentCoachingAdvice(answer.Summary.Trim(), adjustments, Clean(answer.KeyTips, 3)),
            usage);
    }
}
