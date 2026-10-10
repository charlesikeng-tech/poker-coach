using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PokerCoach.Application.Analytics;
using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;
using PokerCoach.Infrastructure.Persistence;
using PokerCoach.Infrastructure.Platform;

namespace PokerCoach.IntegrationTests;

/// <summary>Data-protection keys and feature usage (ADR-0011).</summary>
public sealed class PlatformStoresTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Protected_data_outlives_the_key_ring_held_in_memory()
    {
        var protector = fixture.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test");
        var token = protector.Protect("session");

        // A new instance (another container, or after a restart) only has what the database holds.
        var repository = fixture.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository;
        Assert.IsType<DataProtectionKeyStore>(repository);
        var fresh = DataProtectionProvider.Create(new DirectoryInfo(Path.GetTempPath()), b => b
            .SetApplicationName("poker-coach")
            .AddKeyManagementOptions(o => o.XmlRepository = repository));

        Assert.Equal("session", fresh.CreateProtector("test").Unprotect(token));
    }

    [Fact]
    public void A_key_stored_twice_under_the_same_name_is_kept_once()
    {
        var store = (DataProtectionKeyStore)fixture.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository!;
        var name = $"key-{Guid.NewGuid()}";

        store.StoreElement(new XElement("key", new XAttribute("id", name), "first"), name);
        store.StoreElement(new XElement("key", new XAttribute("id", name), "second"), name);

        var stored = Assert.Single(store.GetAllElements(), e => (string?)e.Attribute("id") == name);
        Assert.Equal("first", stored.Value);
    }

    [Fact]
    public async Task Feature_usage_is_one_row_per_day_purged_by_age_and_deleted_with_the_account()
    {
        var userId = await CreateUserAsync();
        var today = new DateOnly(2026, 10, 10);
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IFeatureUsageStore>();
        var db = scope.ServiceProvider.GetRequiredService<PokerCoachDbContext>();

        await store.RecordAsync(userId, today, "statistics", Ct);
        await store.RecordAsync(userId, today, "statistics", Ct);
        await store.RecordAsync(userId, today.AddDays(-500), "statistics", Ct);
        Assert.Equal(2, await db.Set<FeatureUsageRecord>().CountAsync(u => u.UserId == userId, Ct));

        Assert.True(await store.PurgeBeforeAsync(today.AddDays(-400), Ct) >= 1);
        Assert.Equal(today, (await db.Set<FeatureUsageRecord>().SingleAsync(u => u.UserId == userId, Ct)).Day);

        Assert.True(await scope.ServiceProvider.GetRequiredService<IAccountDataStore>().DeleteUserAsync(userId, Ct));
        Assert.False(await db.Set<FeatureUsageRecord>().AnyAsync(u => u.UserId == userId, Ct));
    }

    private async Task<Guid> CreateUserAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<ExternalSignInService>().SignInAsync(
            new ExternalSignIn(IdentityProviders.Google, Guid.NewGuid().ToString(), null, "Player", "fr"),
            Ct);
        return user.Id;
    }
}
