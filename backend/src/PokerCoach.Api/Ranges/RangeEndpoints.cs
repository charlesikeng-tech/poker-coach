using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Billing;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Subscriptions;
using PokerCoach.Api.Leaks;
using PokerCoach.Api.Statistics;
using PokerCoach.Application.Ranges;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Subscriptions;

namespace PokerCoach.Api.Ranges;

/// <param name="Hand">Starting hand: "AA", "AKs", "AKo".</param>
/// <param name="Dealt">Times dealt when folded to (raise-first-in spot).</param>
/// <param name="InReference">The reference range raises it first in.</param>
public sealed record RangeCellResponse(string Hand, int Dealt, int Opens, int Limps, bool InReference);

/// <param name="Position">Seat by distance to the button within the format: "utg" is three before it at
/// 6-max, six before it at full ring.</param>
/// <param name="ReferenceRate">The position's reference opening rate (ratios), when one exists.</param>
/// <param name="ReferenceNotation">The reference range as written ("22+, A2s+, …").</param>
/// <param name="ReferenceShare">Share of all two-card holdings the reference opens (ratio).</param>
/// <param name="Cells">The 169 starting hands in grid order: row by row from aces, pairs on the diagonal,
/// suited above, offsuit below.</param>
public sealed record PositionRangeResponse(
    PokerPosition Position,
    int Dealt,
    int Opens,
    int Limps,
    StatRateResponse OpenRate,
    ReferenceRangeResponse? ReferenceRate,
    string? ReferenceNotation,
    decimal? ReferenceShare,
    IReadOnlyList<RangeCellResponse> Cells);

/// <param name="Caller">The seat considering the call.</param>
/// <param name="Shover">The seat that shoved all-in.</param>
/// <param name="Cells">169 booleans in grid order: true when equilibrium calls.</param>
public sealed record PushCallRangeResponse(PokerPosition Caller, PokerPosition Shover, IReadOnlyList<bool> Cells);

/// <param name="PendingHands">Hands still being analysed: ranges are partial while above zero.</param>
/// <param name="PushStack">Push/fold band: the stack the computed reference is for.</param>
/// <param name="CallRanges">Push band only: equilibrium calling ranges for every (caller, shover) pair.</param>
public sealed record OpeningRangesResponse(
    TableFormat Format,
    StackBand Band,
    int? PushStack,
    FormatCountsResponse Spots,
    IReadOnlyList<PositionRangeResponse> Positions,
    IReadOnlyList<PushCallRangeResponse> CallRanges,
    int PendingHands,
    int ReferenceVersion);

/// <param name="Reference">What the reference does with it (fold, call, threeBet); null when no reference is written.</param>
public sealed record DefenceCellResponse(string Hand, int Dealt, int Calls, int ThreeBets, DefenceAction? Reference);

/// <param name="Defender">The seat facing the open, named like the opening positions.</param>
/// <param name="DefenceRate">Calls and 3-bets over spots.</param>
public sealed record DefencePairResponse(
    PokerPosition Defender,
    PokerPosition Opener,
    int Dealt,
    int Calls,
    int ThreeBets,
    StatRateResponse DefenceRate,
    StatRateResponse ThreeBetRate,
    bool HasReference);

/// <param name="CallShare">Share of all two-card holdings the reference calls (ratio); null without a reference.</param>
/// <param name="Cells">The 169 starting hands in grid order.</param>
public sealed record DefenceSpotResponse(
    DefencePairResponse Pair,
    string? CallNotation,
    string? ThreeBetNotation,
    decimal? CallShare,
    decimal? ThreeBetShare,
    IReadOnlyList<DefenceCellResponse> Cells);

/// <param name="Pairs">Every defender–opener pair of the format with its counts, openers first to act first.</param>
public sealed record DefenceRangesResponse(
    TableFormat Format,
    StackBand Band,
    FormatCountsResponse Spots,
    IReadOnlyList<DefencePairResponse> Pairs,
    DefenceSpotResponse Selected,
    int PendingHands,
    int ReferenceVersion);

public static class RangeEndpoints
{
    public static IEndpointRouteBuilder MapRangeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/ranges/opening", GetOpeningAsync).WithTags("Ranges").RequiresFeature(Feature.OpeningRanges);
        endpoints.MapGet("/api/ranges/defence", GetDefenceAsync).WithTags("Ranges").RequiresFeature(Feature.DefenceRanges);
        return endpoints;
    }

    /// <summary>
    /// The player's actual opening ranges by position next to the reference ones (ADR-0009), for one stack
    /// band: counts from his own hands, reference version 1.
    /// </summary>
    /// <param name="format">sixMax or fullRing; omitted: the format with the most spots.</param>
    /// <param name="band">push (below 15 BB, computed equilibrium), short (15–25 BB), mid (25–40 BB) or deep (40 BB and more).</param>
    /// <param name="stack">Push band only: stack in big blinds for the equilibrium (3–15, default 10).</param>
    /// <param name="to">Exclusive upper bound on the hand start time.</param>
    private static async Task<Results<Ok<OpeningRangesResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetOpeningAsync(
        HttpContext context,
        PlanAccess plans,
        RangeService ranges,
        CancellationToken cancellationToken,
        string? format = null,
        string band = "mid",
        int? stack = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? gameType = null)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (!Enum.TryParse<StackBand>(band, ignoreCase: true, out var stackBand) || !Enum.IsDefined(stackBand))
        {
            return ApiProblems.Validation("band", "Must be one of: push, short, mid, deep.");
        }

        if (stack is not null && stack is < PushFoldNash.MinStack or > PushFoldNash.MaxStack)
        {
            return ApiProblems.Validation("stack", $"Must be between {PushFoldNash.MinStack} and {PushFoldNash.MaxStack}.");
        }

        if (!TableFormatQuery.TryParse(format, out var tableFormat))
        {
            return ApiProblems.Validation("format", "Must be sixMax or fullRing.");
        }

        if (from is not null && to is not null && from >= to)
        {
            return ApiProblems.Validation("to", "Must be after 'from'.");
        }

        if (!GameTypeQuery.TryParse(gameType, out var parsedGameType))
        {
            return ApiProblems.Validation("gameType", "Must be tournament or cashGame.");
        }

        // The push/fold equilibrium is an Elite feature (ADR-0016).
        if (stackBand == StackBand.Push && !(await plans.GetPlanAsync(userId, cancellationToken)).Allows(Feature.PushFoldRanges))
        {
            return PlanGates.Required(Feature.PushFoldRanges);
        }

        // Free plan: history window (ADR-0014). Data is kept, only not shown.
        from = await plans.ClampFromAsync(userId, from, cancellationToken);
        var result = await ranges.GetOpeningAsync(userId, tableFormat, stackBand, stack, from, to, cancellationToken, parsedGameType);
        return TypedResults.Ok(new OpeningRangesResponse(
            result.Format,
            result.Band,
            result.PushStack,
            new FormatCountsResponse(result.SpotsByFormat[TableFormat.SixMax], result.SpotsByFormat[TableFormat.FullRing]),
            result.Positions.Select(p => new PositionRangeResponse(
                p.Position,
                p.Dealt,
                p.Opens,
                p.Limps,
                new StatRateResponse(p.OpenRate.Made, p.OpenRate.Opportunities, p.OpenRate.Rate),
                p.ReferenceRate is { } r ? new ReferenceRangeResponse(r.Min, r.Max) : null,
                p.ReferenceNotation,
                p.ReferenceShare,
                p.Cells.Select(c => new RangeCellResponse(c.Hand.ToString(), c.Dealt, c.Opens, c.Limps, c.InReference)).ToList())).ToList(),
            result.CallRanges.Select(cr => new PushCallRangeResponse(cr.Caller, cr.Shover, cr.Cells)).ToList(),
            result.PendingHands,
            result.ReferenceVersion));
    }

    /// <summary>
    /// The player's defence against a single open next to the reference (ADR-0018): for one defender and one
    /// opener, what each starting hand did (call, 3-bet, fold), with the list of every pair and its counts.
    /// </summary>
    /// <param name="band">short (15–25 BB, no reference written), mid (25–40 BB) or deep (40 BB and more).</param>
    /// <param name="defender">Seat facing the open; with <paramref name="opener"/>, omitted or invalid: the big blind's busiest spot.</param>
    private static async Task<Results<Ok<DefenceRangesResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetDefenceAsync(
        HttpContext context,
        PlanAccess plans,
        DefenceRangeService defence,
        CancellationToken cancellationToken,
        string? format = null,
        string band = "mid",
        string? defender = null,
        string? opener = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? gameType = null)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (!Enum.TryParse<StackBand>(band, ignoreCase: true, out var stackBand) || !Enum.IsDefined(stackBand) || stackBand == StackBand.Push)
        {
            return ApiProblems.Validation("band", "Must be one of: short, mid, deep.");
        }

        if (!TableFormatQuery.TryParse(format, out var tableFormat))
        {
            return ApiProblems.Validation("format", "Must be sixMax or fullRing.");
        }

        if (from is not null && to is not null && from >= to)
        {
            return ApiProblems.Validation("to", "Must be after 'from'.");
        }

        if (!GameTypeQuery.TryParse(gameType, out var parsedGameType))
        {
            return ApiProblems.Validation("gameType", "Must be tournament or cashGame.");
        }

        if (!TryParseSeat(defender, out var defenderSeat))
        {
            return ApiProblems.Validation("defender", "Must be a position (utg … bigBlind).");
        }

        if (!TryParseSeat(opener, out var openerSeat))
        {
            return ApiProblems.Validation("opener", "Must be a position (utg … smallBlind).");
        }

        from = await plans.ClampFromAsync(userId, from, cancellationToken);
        var result = await defence.GetAsync(userId, tableFormat, stackBand, defenderSeat, openerSeat, from, to, cancellationToken, parsedGameType);
        var selected = result.Selected;
        return TypedResults.Ok(new DefenceRangesResponse(
            result.Format,
            result.Band,
            new FormatCountsResponse(result.SpotsByFormat[TableFormat.SixMax], result.SpotsByFormat[TableFormat.FullRing]),
            result.Pairs.Select(Pair).ToList(),
            new DefenceSpotResponse(
                Pair(selected.Pair),
                selected.CallNotation,
                selected.ThreeBetNotation,
                selected.CallShare,
                selected.ThreeBetShare,
                selected.Cells.Select(c => new DefenceCellResponse(c.Hand.ToString(), c.Dealt, c.Calls, c.ThreeBets, c.Reference)).ToList()),
            result.PendingHands,
            result.ReferenceVersion));
    }

    /// <summary>Omitted is valid (null); a name in any case is; a number or an unknown name is not.</summary>
    private static bool TryParseSeat(string? value, out PokerPosition? seat)
    {
        seat = null;
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        if (int.TryParse(value, out _) || !Enum.TryParse<PokerPosition>(value, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            return false;
        }

        seat = parsed;
        return true;
    }

    private static DefencePairResponse Pair(DefencePair p) => new(
        p.Defender,
        p.Opener,
        p.Dealt,
        p.Calls,
        p.ThreeBets,
        new StatRateResponse(p.DefenceRate.Made, p.DefenceRate.Opportunities, p.DefenceRate.Rate),
        new StatRateResponse(p.ThreeBetRate.Made, p.ThreeBetRate.Opportunities, p.ThreeBetRate.Rate),
        p.HasReference);
}
