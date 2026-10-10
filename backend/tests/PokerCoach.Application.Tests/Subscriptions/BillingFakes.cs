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
    /// <summary>Billing off: every account has Pro, as in every test written before plans existed.</summary>
    public static PlanAccess Unbilled(TimeProvider time) =>
        new(new InMemorySubscriptionStore(), new FakeBillingGateway(configured: false), new FreePlanOptions(), time);
}
