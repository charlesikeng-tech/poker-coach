using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Bankroll;
using PokerCoach.Domain.Bankroll;

namespace PokerCoach.Api.Bankroll;

public sealed record BankrollSettingsResponse(decimal StartingAmount, DateTimeOffset StartedOn, BuyInRule Rule, int BuyIns);

public sealed record BuyInLimitResponse(decimal MaxAverageBuyIn, decimal? RecentAverageBuyIn, int AboveLimitRecently, int PlayedRecently);

/// <param name="Label">Tournament name or the movement's note.</param>
public sealed record BalancePointResponse(DateTimeOffset At, decimal Balance, decimal Change, BalanceEvent Event, string? Label);

public sealed record BankrollMovementResponse(Guid Id, BankrollMovementKind Kind, decimal Amount, DateTimeOffset OccurredAt, string? Note)
{
    public static BankrollMovementResponse From(BankrollMovement m) => new(m.Id, m.Kind, m.Amount, m.OccurredAt, m.Note);
}

/// <param name="Mean">0.1 = +10 %.</param>
/// <param name="Low">Lower bound of the 95 % interval.</param>
public sealed record RoiEstimateResponse(int Tournaments, decimal Mean, decimal Low, decimal High);

/// <param name="HalfLoss">Probability (0–1) of losing half of the bankroll within <c>tournaments</c>.</param>
/// <param name="Ruin">Probability (0–1) of losing it all.</param>
/// <param name="TypicalDownswing">Median worst drop from a peak, in average buy-ins.</param>
public sealed record RiskOutlookResponse(
    int Tournaments,
    decimal AverageBuyIn,
    decimal HalfLoss,
    decimal Ruin,
    decimal FinalLow,
    decimal FinalMedian,
    decimal FinalHigh,
    decimal TypicalDownswing);

/// <param name="Risk">Null below <c>minTournamentsForRisk</c> results or with nothing left.</param>
public sealed record BankrollSummaryResponse(
    decimal Balance,
    decimal TournamentProfit,
    decimal NetDeposits,
    int Tournaments,
    int UnknownResults,
    BuyInLimitResponse Limit,
    IReadOnlyList<BalancePointResponse> Curve,
    IReadOnlyList<BankrollMovementResponse> Movements,
    RoiEstimateResponse Roi,
    RiskOutlookResponse? Risk,
    int MinTournamentsForRisk);

/// <summary>Settings and summary are null until the player sets up his bankroll.</summary>
public sealed record BankrollResponse(BankrollSettingsResponse? Settings, BankrollSummaryResponse? Summary);

public sealed record BankrollSettingsRequest(decimal? StartingAmount, DateTimeOffset? StartedOn, BuyInRule? Rule);

/// <param name="Amount">Positive for deposits and withdrawals; signed for adjustments.</param>
public sealed record BankrollMovementRequest(BankrollMovementKind? Kind, decimal? Amount, DateTimeOffset? OccurredAt, string? Note);

/// <summary>The bankroll: balance, buy-in limit, risk of ruin (roadmap step 1). Amounts are in euros.</summary>
public static class BankrollEndpoints
{
    public const string NotConfigured = "BANKROLL_NOT_CONFIGURED";
    public const string MovementNotFound = "BANKROLL_MOVEMENT_NOT_FOUND";

    /// <summary>Far above any real bankroll: catches a typo (an extra zero row) without limiting anyone.</summary>
    private const decimal MaxAmount = 10_000_000m;

    /// <summary>Clocks differ: a movement dated a few minutes ahead is still "now".</summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapBankrollEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/bankroll").WithTags("Bankroll");
        group.MapGet("", GetAsync);
        group.MapPut("/settings", SaveSettingsAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapPost("/movements", AddMovementAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapDelete("/movements/{id:guid}", DeleteMovementAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        return endpoints;
    }

    private static async Task<Results<Ok<BankrollResponse>, UnauthorizedHttpResult>> GetAsync(
        HttpContext context,
        BankrollService bankroll,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var overview = await bankroll.GetAsync(userId, cancellationToken);
        return TypedResults.Ok(overview is null ? new BankrollResponse(null, null) : ToResponse(overview));
    }

    private static async Task<Results<NoContent, ProblemHttpResult, UnauthorizedHttpResult>> SaveSettingsAsync(
        BankrollSettingsRequest request,
        HttpContext context,
        BankrollService bankroll,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (request.StartingAmount is not ({ } amount and >= 0 and <= MaxAmount))
        {
            return ApiProblems.Validation("startingAmount", $"Required, between 0 and {MaxAmount}.");
        }

        if (request.StartedOn is not { } startedOn || startedOn > time.GetUtcNow() + ClockSkew)
        {
            return ApiProblems.Validation("startedOn", "Required, not in the future.");
        }

        if (request.Rule is not { } rule || !Enum.IsDefined(rule))
        {
            return ApiProblems.Validation("rule", "Must be conservative, standard or aggressive.");
        }

        await bankroll.SaveSettingsAsync(userId, new BankrollSettings(Math.Round(amount, 2), startedOn.ToUniversalTime(), rule), cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created<BankrollMovementResponse>, ProblemHttpResult, UnauthorizedHttpResult>> AddMovementAsync(
        BankrollMovementRequest request,
        HttpContext context,
        BankrollService bankroll,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (request.Kind is not { } kind || !Enum.IsDefined(kind))
        {
            return ApiProblems.Validation("kind", "Must be deposit, withdrawal or adjustment.");
        }

        var validAmount = request.Amount is { } a && Math.Abs(a) <= MaxAmount
            && (kind == BankrollMovementKind.Adjustment ? Math.Round(a, 2) != 0 : Math.Round(a, 2) > 0);
        if (!validAmount)
        {
            return ApiProblems.Validation("amount", kind == BankrollMovementKind.Adjustment
                ? "Required, not zero."
                : "Required, positive: the kind gives the direction.");
        }

        if (request.OccurredAt is not { } occurredAt || occurredAt > time.GetUtcNow() + ClockSkew)
        {
            return ApiProblems.Validation("occurredAt", "Required, not in the future.");
        }

        if (request.Note is { Length: > BankrollService.MaxNoteLength })
        {
            return ApiProblems.Validation("note", $"At most {BankrollService.MaxNoteLength} characters.");
        }

        var (outcome, movement) = await bankroll.AddMovementAsync(userId, kind, request.Amount!.Value, occurredAt.ToUniversalTime(), request.Note, cancellationToken);
        return outcome switch
        {
            MovementOutcome.NotConfigured => ApiProblems.WithCode(StatusCodes.Status409Conflict, NotConfigured, "Set up the bankroll first."),
            MovementOutcome.BeforeStart => ApiProblems.Validation("occurredAt", "Before the bankroll's start date: it would not count."),
            _ => TypedResults.Created($"/api/bankroll/movements/{movement!.Id}", BankrollMovementResponse.From(movement)),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult, UnauthorizedHttpResult>> DeleteMovementAsync(
        Guid id,
        HttpContext context,
        BankrollService bankroll,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        return await bankroll.DeleteMovementAsync(userId, id, cancellationToken)
            ? TypedResults.NoContent()
            : ApiProblems.WithCode(StatusCodes.Status404NotFound, MovementNotFound, "Movement not found.");
    }

    private static BankrollResponse ToResponse(BankrollOverview o)
    {
        var s = o.Settings;
        var risk = o.Risk;
        return new BankrollResponse(
            new BankrollSettingsResponse(s.StartingAmount, s.StartedOn, s.Rule, s.BuyIns),
            new BankrollSummaryResponse(
                o.Balance,
                o.TournamentProfit,
                o.NetDeposits,
                o.Tournaments,
                o.UnknownResults,
                new BuyInLimitResponse(o.Limit.MaxAverageBuyIn, o.Limit.RecentAverageBuyIn, o.Limit.AboveLimitRecently, o.Limit.PlayedRecently),
                o.Curve.Select(p => new BalancePointResponse(p.At, p.Balance, p.Change, p.Event, p.Label)).ToList(),
                o.Movements.Select(BankrollMovementResponse.From).ToList(),
                new RoiEstimateResponse(o.Roi.Tournaments, o.Roi.Mean, o.Roi.Low, o.Roi.High),
                risk is null
                    ? null
                    : new RiskOutlookResponse(risk.Tournaments, risk.AverageBuyIn, risk.HalfLoss, risk.Ruin, risk.FinalLow, risk.FinalMedian, risk.FinalHigh, risk.TypicalDownswing),
                RiskSimulation.MinTournaments));
    }
}
