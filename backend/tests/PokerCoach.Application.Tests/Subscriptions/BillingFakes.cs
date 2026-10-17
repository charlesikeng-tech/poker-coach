using PokerCoach.Application.Subscriptions;
using PokerCoach.Domain.Subscriptions;

namespace PokerCoach.Application.Tests.Subscriptions;

internal sealed class InMemorySubscriptionStore : ISubscriptionStore
{
    public List<Subscription> Rows { get; } = [];

    public int Saves { get; private set; }

    public Task<Subscription?> FindByUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.FirstOrDefault(s => s.UserId == userId));

    public Task<Subscription?> FindByCustomerAsync(string customerId, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.FirstOrDefault(s => s.CustomerId == customerId));

    public Task<bool> TryAddAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        if (Rows.Any(s => s.UserId == subscription.UserId || s.CustomerId == subscription.CustomerId))
        {
            return Task.FromResult(false);
        }

        Rows.Add(subscription);
        return Task.FromResult(true);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }

    public List<ProGrant> Grants { get; } = [];

    public Task<ProGrant?> FindGrantAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Grants.FirstOrDefault(g => g.UserId == userId));

    public Task SaveGrantAsync(ProGrant grant, CancellationToken cancellationToken)
    {
        if (Grants.FirstOrDefault(g => g.UserId == grant.UserId) is { } current)
        {
            current.Renew(grant);
        }
        else
        {
            Grants.Add(grant);
        }

        return Task.CompletedTask;
    }

    public Task<bool> DeleteGrantAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Grants.RemoveAll(g => g.UserId == userId) > 0);

    public Task<IReadOnlyList<GrantView>> ListGrantsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GrantView>>([.. Grants.Select(g => new GrantView(g.UserId, "user", null, g.Plan, g.Reason, g.EndsAt, g.GrantedAt))]);

    public Task<IReadOnlyList<SubscriberView>> ListSubscribersAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SubscriberView>>(
            [.. Rows.Where(r => r.Status is not null).Take(limit).Select(r => new SubscriberView(r.UserId, "user", null, r.CustomerId, r.Status, r.Tier, r.Interval, r.CurrentPeriodEnd, r.CancelAtPeriodEnd))]);
}

/// <summary>Stripe stand-in: customers and their subscriptions in memory; a valid signature is the string "valid".</summary>
internal sealed class FakeBillingGateway(bool configured = true) : IBillingGateway
{
    private int customers;

    public bool IsConfigured => configured;

    public Dictionary<string, List<SubscriptionState>> Subscriptions { get; } = [];

    public List<CheckoutRequest> Checkouts { get; } = [];

    public List<string> Deleted { get; } = [];

    public bool Failing { get; set; }

    public Task<string> CreateCustomerAsync(Guid userId, string? email, CancellationToken cancellationToken)
    {
        Fail();
        var id = $"cus_{++customers}";
        Subscriptions[id] = [];
        return Task.FromResult(id);
    }

    public Task<Uri> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        Fail();
        Checkouts.Add(request);
        return Task.FromResult(new Uri($"https://checkout.stripe.test/{request.CustomerId}/{request.Interval}"));
    }

    /// <summary>Refuse the confirmation shortcut only (as Stripe does when the portal cannot switch plans).</summary>
    public bool RefusesPlanChange { get; set; }

    public List<(string SubscriptionId, Plan Plan, BillingInterval Interval)> PlanChanges { get; } = [];

    public Task<Uri> CreatePlanChangeAsync(string customerId, string subscriptionId, Plan plan, BillingInterval interval, Uri returnUrl, CancellationToken cancellationToken)
    {
        Fail();
        if (RefusesPlanChange)
        {
            throw new BillingGatewayException("refused");
        }

        PlanChanges.Add((subscriptionId, plan, interval));
        return Task.FromResult(new Uri($"https://billing.stripe.test/{customerId}/change/{plan}/{interval}"));
    }

    public Task<Uri> CreatePortalAsync(string customerId, Uri returnUrl, CancellationToken cancellationToken)
    {
        Fail();
        return Task.FromResult(new Uri($"https://billing.stripe.test/{customerId}"));
    }

    public Task<IReadOnlyList<SubscriptionState>> ListSubscriptionsAsync(string customerId, CancellationToken cancellationToken)
    {
        Fail();
        return Task.FromResult<IReadOnlyList<SubscriptionState>>(Subscriptions.GetValueOrDefault(customerId) ?? []);
    }

    public Task DeleteCustomerAsync(string customerId, CancellationToken cancellationToken)
    {
        Fail();
        Deleted.Add(customerId);
        return Task.CompletedTask;
    }

    public List<TopUpRequest> TopUps { get; } = [];

    public Task<Uri> CreateTopUpCheckoutAsync(TopUpRequest request, CancellationToken cancellationToken)
    {
        Fail();
        TopUps.Add(request);
        return Task.FromResult(new Uri($"https://checkout.stripe.test/topup/{request.Credits}"));
    }

    public BillingEvent? ReadWebhook(string payload, string? signatureHeader) =>
        signatureHeader == "valid" ? new BillingEvent("evt_1", "customer.subscription.updated", payload.Length == 0 ? null : payload) : null;

    private void Fail()
    {
        if (Failing)
        {
            throw new BillingGatewayException("Stripe answered 500: api_error");
        }
    }
}

internal static class BillingFakes
{
    /// <summary>Billing off: every account has Elite, as in every test written before plans existed.</summary>
    public static PlanAccess Unbilled(TimeProvider time) =>
        new(new InMemorySubscriptionStore(), new FakeBillingGateway(configured: false), new Identity.InMemoryUserAccountStore(), new AdminOptions(), new PlanOptions(), time);
}
