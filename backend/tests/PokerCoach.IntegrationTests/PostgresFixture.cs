using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PokerCoach.Application.Identity;
using PokerCoach.Application.Import;
using PokerCoach.Application.Poker;
using PokerCoach.HandHistories;
using PokerCoach.HandHistories.Winamax;
using PokerCoach.Infrastructure;
using PokerCoach.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PokerCoach.IntegrationTests.PostgresFixture))]

// Tests share one database and the import queue: run them one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PokerCoach.IntegrationTests;

/// <summary>
/// One PostgreSQL container for the whole run, schema created by the real migrations (they are tested
/// too). Tests isolate their data by creating their own user.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder()
        .WithImage("postgres:18-alpine")
        .Build();

    private ServiceProvider? services;

    public IServiceProvider Services => services ?? throw new InvalidOperationException("Fixture not initialized.");

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:PokerCoach"] = container.GetConnectionString() })
            .Build();

        // Same registrations as the API host (Program.cs), minus HTTP.
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddInfrastructure(configuration);
        collection.AddSingleton(TimeProvider.System);
        collection.AddSingleton(new ImportOptions { WorkerEnabled = false });
        collection.AddSingleton<IHandHistoryProvider, WinamaxHandHistoryProvider>();
        collection.AddScoped<ImportService>();
        collection.AddScoped<ImportProcessor>();
        collection.AddScoped<PokerAccountService>();
        collection.AddScoped<ExternalSignInService>();
        services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PokerCoachDbContext>().Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (services is not null)
        {
            await services.DisposeAsync();
        }

        await container.DisposeAsync();
    }
}
