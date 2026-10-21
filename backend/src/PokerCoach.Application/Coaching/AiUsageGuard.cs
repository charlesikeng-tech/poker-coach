using PokerCoach.Application.Subscriptions;
using PokerCoach.Domain.Subscriptions;

namespace PokerCoach.Application.Coaching;

/// <summary>What the coach can be asked to write. Each kind is priced in credits (ADR-0016).</summary>
public enum AiTask
{
    LeakExplanation,
    WeekReview,
    TournamentDebrief,
    CashDebrief,
    BankrollCoach,
    MonthlyReport,
    TournamentSelection,
}

public static class AiTasks
{
    /// <summary>Kept as the ledger's purpose column: stable strings, independent of the enum's names.</summary>
    public static string Purpose(this AiTask task) => task switch
    {
        AiTask.TournamentDebrief => "tournament-debrief",
        AiTask.CashDebrief => "cash-debrief",
        AiTask.WeekReview => "week-review",
        AiTask.BankrollCoach => "bankroll-coach",
        AiTask.MonthlyReport => "monthly-report",
        AiTask.TournamentSelection => "tournament-selection",
        _ => "leak-explanation",
    };

    /// <summary>Credits charged once the call succeeded: about what each kind costs us, relative to an explanation.</summary>
    public static int Credits(this AiTask task) => task switch
    {
        AiTask.TournamentDebrief => 5,
        AiTask.CashDebrief => 5,
        AiTask.WeekReview => 2,
        AiTask.MonthlyReport => 3,
        AiTask.BankrollCoach => 1,
        AiTask.TournamentSelection => 2,
        _ => 1,
    };

    /// <summary>The feature the plans gate; null: every plan, within its credits.</summary>
    public static Feature? Feature(this AiTask task) => task switch
    {
        AiTask.TournamentDebrief => Domain.Subscriptions.Feature.TournamentDebrief,
        AiTask.CashDebrief => Domain.Subscriptions.Feature.TournamentDebrief,
        AiTask.WeekReview => Domain.Subscriptions.Feature.WeekReview,
        AiTask.MonthlyReport => Domain.Subscriptions.Feature.MonthlyReport,
        _ => null,
    };
}

/// <summary>A user's AI usage as seen inside the reservation lock (ADR-0017).</summary>
/// <param name="CreditsUsedThisMonth">Billed calls plus live reservations.</param>
/// <param name="CallsToday">Calls of this task today, failed ones included: a provider failure loop still costs.</param>
/// <param name="CallsThisMonth">Billed calls and live reservations of this task this month.</param>
/// <param name="SpendThisMonthUsd">Every user: real cost of settled calls, reserved ceiling of the others.</param>
/// <param name="PurchasedCreditsBalance">Remaining credits from one-time top-up purchases.</param>
public sealed record AiUsageSnapshot(int CreditsUsedThisMonth, int CallsToday, int CallsThisMonth, decimal SpendThisMonthUsd, int PurchasedCreditsBalance = 0);

/// <param name="LiveAfter">Reservations older than this are abandoned (crash): their credits are freed, their ceiling still counts as spent.</param>
/// <param name="AlertAtUsd">The ledger warns when the month's spend crosses this.</param>
/// <param name="MonthlyCredits">Plan monthly limit; null for admin accounts (no limit). Used to calculate how many purchased credits this call consumes.</param>
public sealed record AiReservationRequest(
    Guid Id,
    Guid UserId,
    AiTask Task,
    int Credits,
    decimal CeilingUsd,
    DateTimeOffset Now,
    DateTimeOffset MonthStart,
    DateTimeOffset DayStart,
    DateTimeOffset LiveAfter,
    decimal AlertAtUsd,
    int? MonthlyCredits = null);

/// <param name="Usage">Null when the provider's billing is unknown (network failure, timeout): the reserved ceiling stays as the cost.</param>
public sealed record AiSettlement(bool Succeeded, int Credits, ModelUsage? Usage, decimal? CostUsd, DateTimeOffset At);

/// <param name="CostUsd">Every settled call, failed ones included: what the task really costs us.</param>
public sealed record AiTaskUsage(AiTask Task, int Billed, int Failed, int UnknownCost, int Credits, decimal CostUsd)
{
    /// <summary>What one sold credit costs us for this task; the number to compare with the offers' prices (ADR-0016).</summary>
    public decimal? CostPerCreditUsd => Credits > 0 ? Math.Round(CostUsd / Credits, 6) : null;
}

public sealed record AiUserUsage(Guid? UserId, string? Email, int Credits, decimal CostUsd);

/// <param name="OpenReservationsUsd">Ceilings of calls in flight or abandoned: counted in the spend until settled.</param>
public sealed record AiUsageReport(decimal SpendUsd, decimal OpenReservationsUsd, IReadOnlyList<AiTaskUsage> Tasks, IReadOnlyList<AiUserUsage> TopUsers);

/// <summary>The month's AI usage against its budget, for administrators.</summary>
public sealed record AiUsageOverview(DateTimeOffset MonthStart, decimal BudgetUsd, AiUsageReport Report);

/// <summary>The AI usage ledger (ADR-0017). Reservation and its checks are atomic across concurrent requests.</summary>
public interface IAiUsageLedger
{
    /// <summary>
    /// Under a lock shared by every reservation: reads the snapshot, asks <paramref name="decide"/>, and records the
    /// reservation when it returns null. Returns the refusal otherwise.
    /// </summary>
    Task<CoachingFailure?> ReserveAsync(AiReservationRequest request, Func<AiUsageSnapshot, CoachingFailure?> decide, CancellationToken cancellationToken);

    /// <summary>Settles a reservation once; a second settlement of the same reservation is ignored.</summary>
    Task SettleAsync(Guid reservationId, AiSettlement settlement, CancellationToken cancellationToken);

    Task<int> CreditsUsedAsync(Guid userId, DateTimeOffset since, DateTimeOffset liveAfter, CancellationToken cancellationToken);

    Task<AiUsageReport> ReportAsync(DateTimeOffset since, int topUsers, CancellationToken cancellationToken);

    /// <summary>Credits a user's purchased balance. Idempotent: a duplicate stripeEventId is a no-op.</summary>
    Task AddPurchasedCreditsAsync(Guid userId, int credits, string stripeEventId, CancellationToken cancellationToken);

    /// <summary>Remaining credits from one-time purchases (never negative).</summary>
    Task<int> PurchasedCreditsBalanceAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>The outcome of a guarded call: the model's answer, or why it was refused or failed.</summary>
public sealed record AiOutcome<T>(ModelAnswer<T>? Answer, CoachingFailure? Failure);

/// <summary>
/// The only way to the AI provider (ADR-0017). Before the call, in one atomic step: the model is configured, the
/// plan opens the task, the month's credits cover it, the daily limit and Pro's monthly review allow it, and the
/// global budget has room for the call's worst-case cost, which is reserved. After the call, the real cost is
/// recorded, failed calls included; credits are charged only when the call succeeded.
/// </summary>
public sealed class AiUsageGuard(IAiUsageLedger ledger, ICoachingModel model, CoachingOptions options, PlanAccess plans, TimeProvider time)
{
    public const int ProReviewsPerMonth = 1;
    public const int ProMonthlyReportsPerMonth = 1;

    /// <summary>Longer than any provider call (timeouts included); then the reservation is considered abandoned.</summary>
    public static readonly TimeSpan ReservationLifetime = TimeSpan.FromMinutes(10);

    /// <summary>Share of the monthly budget at which the ledger logs a warning.</summary>
    public const decimal AlertShare = 0.8m;

    public static DateTimeOffset MonthStart(DateTimeOffset now) => new(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

    public Task<int> CreditsUsedThisMonthAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return ledger.CreditsUsedAsync(userId, MonthStart(now), now - ReservationLifetime, cancellationToken);
    }

    public Task<int> PurchasedCreditsBalanceAsync(Guid userId, CancellationToken cancellationToken) =>
        ledger.PurchasedCreditsBalanceAsync(userId, cancellationToken);

    public Task AddPurchasedCreditsAsync(Guid userId, int credits, string stripeEventId, CancellationToken cancellationToken) =>
        ledger.AddPurchasedCreditsAsync(userId, credits, stripeEventId, cancellationToken);

    public async Task<AiUsageOverview> MonthOverviewAsync(int topUsers, CancellationToken cancellationToken)
    {
        var start = MonthStart(time.GetUtcNow());
        return new AiUsageOverview(start, options.MonthlyBudgetUsd, await ledger.ReportAsync(start, topUsers, cancellationToken));
    }

    /// <param name="work">
    /// Prepares the prompt and calls the model, nothing after: it runs only once the call is reserved, so a refused
    /// request costs no preparation.
    /// </param>
    public async Task<AiOutcome<T>> RunAsync<T>(
        Guid userId,
        AiTask task,
        Func<ICoachingModel, CancellationToken, Task<ModelAnswer<T>>> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (!model.IsAvailable)
        {
            return new AiOutcome<T>(null, CoachingFailure.Unavailable);
        }

        var access = await plans.GetAccessAsync(userId, cancellationToken);
        if (task.Feature() is { } feature && !access.Plan.Allows(feature))
        {
            return new AiOutcome<T>(null, CoachingFailure.PlanRequired);
        }

        var now = time.GetUtcNow();
        var ceiling = options.CostOf(model.Ceiling(task));
        var monthlyCredits = access.Source == PlanSource.Admin ? (int?)null : plans.Limits.For(access.Plan).MonthlyCredits;
        var request = new AiReservationRequest(
            Guid.CreateVersion7(now),
            userId,
            task,
            task.Credits(),
            ceiling,
            now,
            MonthStart(now),
            new DateTimeOffset(now.Date, TimeSpan.Zero),
            now - ReservationLifetime,
            options.MonthlyBudgetUsd * AlertShare,
            monthlyCredits);

        var refused = await ledger.ReserveAsync(request, usage => Decide(usage, task, access.Plan, monthlyCredits, ceiling), cancellationToken);
        if (refused is { } failure)
        {
            return new AiOutcome<T>(null, failure);
        }

        ModelAnswer<T> answer;
        try
        {
            answer = await work(model, cancellationToken);
        }
        catch (CoachingModelException exception)
        {
            await SettleAsync(request, succeeded: false, exception.Usage);
            return new AiOutcome<T>(null, CoachingFailure.ModelFailed);
        }
        catch
        {
            // Preparation or cancellation: whether the provider billed is unknown, the ceiling stays counted.
            await SettleAsync(request, succeeded: false, usage: null);
            throw;
        }

        await SettleAsync(request, succeeded: true, answer.Usage);
        return new AiOutcome<T>(answer, null);
    }

    private CoachingFailure? Decide(AiUsageSnapshot usage, AiTask task, Plan plan, int? monthlyCredits, decimal ceiling)
    {
        if (task == AiTask.MonthlyReport && plan == Plan.Pro && usage.CallsThisMonth >= ProMonthlyReportsPerMonth)
        {
            return CoachingFailure.PlanLimitReached;
        }

        if (task == AiTask.WeekReview && plan == Plan.Pro && usage.CallsThisMonth >= ProReviewsPerMonth)
        {
            return CoachingFailure.PlanLimitReached;
        }

        if (monthlyCredits is { } credits && usage.CreditsUsedThisMonth + task.Credits() > credits + usage.PurchasedCreditsBalance)
        {
            return CoachingFailure.PlanLimitReached;
        }

        if (usage.CallsToday >= DailyLimit(task))
        {
            return CoachingFailure.DailyLimitReached;
        }

        return usage.SpendThisMonthUsd + ceiling > options.MonthlyBudgetUsd ? CoachingFailure.BudgetExhausted : null;
    }

    private int DailyLimit(AiTask task) => task switch
    {
        AiTask.TournamentDebrief or AiTask.CashDebrief => options.DailyDebriefsPerUser,
        AiTask.WeekReview or AiTask.MonthlyReport => options.DailyWeekReviewsPerUser,
        _ => options.DailyExplanationsPerUser,
    };

    /// <summary>Never cancelled with the request: once the provider is called, the cost must reach the ledger.</summary>
    private Task SettleAsync(AiReservationRequest request, bool succeeded, ModelUsage? usage) =>
        ledger.SettleAsync(
            request.Id,
            new AiSettlement(
                succeeded,
                succeeded ? request.Credits : 0,
                usage,
                usage is null ? null : options.CostOf(usage),
                time.GetUtcNow()),
            CancellationToken.None);
}
