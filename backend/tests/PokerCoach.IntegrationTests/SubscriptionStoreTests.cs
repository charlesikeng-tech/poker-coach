using Microsoft.Extensions.DependencyInjection;
using PokerCoach.Application.Identity;
using PokerCoach.Application.Subscriptions;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Subscriptions;

namespace PokerCoach.IntegrationTests;

/// <summary>Billing records on PostgreSQL (ADR-0014): one per user, one user per customer, gone with the account.</summary>
public sealed class SubscriptionStoreTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task One_record_per_user_and_per_customer_kept_in_sync_and_deleted_with_the_account()
    {
        var now = DateTimeOffset.UtcNow;
        var customer = $"cus_{Guid.NewGuid():N}";
        var userId = await AddUserAsync(now);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISubscriptionStore>();
            Assert.True(await store.TryAddAsync(Subscription.Open(userId, customer, now), Ct));
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISubscriptionStore>();

            // A concurrent checkout's second customer for the same user, or a customer reused by another user: refused.
            Assert.False(await store.TryAddAsync(Subscription.Open(userId, $"cus_{Guid.NewGuid():N}", now), Ct));
            Assert.False(await store.TryAddAsync(Subscription.Open(await AddUserAsync(now), customer, now), Ct));

            var row = await store.FindByCustomerAsync(customer, Ct);
            row!.Sync(new SubscriptionState("sub_1", SubscriptionStatus.PastDue, BillingInterval.Year, now.AddDays(300), true), now);
            await store.SaveChangesAsync(Ct);
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<ISubscriptionStore>().FindByUserAsync(userId, Ct);
            Assert.Equal((SubscriptionStatus.PastDue, BillingInterval.Year, true, Plan.Pro), (row!.Status, row.Interval, row.CancelAtPeriodEnd, row.Plan));

            Assert.True(await scope.ServiceProvider.GetRequiredService<IAccountDataStore>().DeleteUserAsync(userId, Ct));
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            Assert.Null(await scope.ServiceProvider.GetRequiredService<ISubscriptionStore>().FindByCustomerAsync(customer, Ct));
        }
    }

    [Fact]
    public async Task Grants_are_replaced_listed_with_their_account_revoked_and_gone_with_the_account()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = await AddUserAsync(now);
        var admin = Guid.NewGuid();

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISubscriptionStore>();
            await store.SaveGrantAsync(ProGrant.Give(userId, "Partenaire", now.AddDays(30), admin, now), Ct);
            await store.SaveGrantAsync(ProGrant.Give(userId, "Partenaire, prolongé", null, admin, now), Ct);
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISubscriptionStore>();
            var grant = Assert.Single(await store.ListGrantsAsync(Ct), g => g.UserId == userId);
            Assert.Equal(("Partenaire, prolongé", (DateTimeOffset?)null, "Jo"), (grant.Reason, grant.EndsAt, grant.DisplayName));

            Assert.True(await store.DeleteGrantAsync(userId, Ct));
            Assert.Null(await store.FindGrantAsync(userId, Ct));

            await store.SaveGrantAsync(ProGrant.Give(userId, "Ami", null, admin, now), Ct);
            Assert.True(await scope.ServiceProvider.GetRequiredService<IAccountDataStore>().DeleteUserAsync(userId, Ct));
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            Assert.Null(await scope.ServiceProvider.GetRequiredService<ISubscriptionStore>().FindGrantAsync(userId, Ct));
        }
    }

    [Fact]
    public async Task Subscribers_are_the_accounts_that_subscribed_at_least_once()
    {
        var now = DateTimeOffset.UtcNow;
        var paying = await AddUserAsync(now);
        var browsing = await AddUserAsync(now);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISubscriptionStore>();
            var subscription = Subscription.Open(paying, $"cus_{Guid.NewGuid():N}", now);
            subscription.Sync(new SubscriptionState("sub_1", SubscriptionStatus.Active, BillingInterval.Month, now.AddDays(30), false), now);
            await store.TryAddAsync(subscription, Ct);
            await store.TryAddAsync(Subscription.Open(browsing, $"cus_{Guid.NewGuid():N}", now), Ct);
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var subscribers = await scope.ServiceProvider.GetRequiredService<ISubscriptionStore>().ListSubscribersAsync(500, Ct);
            Assert.Contains(subscribers, s => s.UserId == paying && s.Status == SubscriptionStatus.Active);
            Assert.DoesNotContain(subscribers, s => s.UserId == browsing);
        }
    }

    private async Task<Guid> AddUserAsync(DateTimeOffset now)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var user = User.Register("Jo", $"{Guid.NewGuid():N}@example.com", "fr", now);
        Assert.True(await scope.ServiceProvider.GetRequiredService<IUserAccountStore>()
            .TryAddAsync(user, ExternalIdentity.Link(user, IdentityProviders.Google, Guid.NewGuid().ToString("N"), user.Email, now), Ct));
        return user.Id;
    }
}
