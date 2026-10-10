using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PokerCoach.Application.Identity;
using PokerCoach.Application.Import;
using PokerCoach.Application.Poker;
using PokerCoach.Application.Statistics;
using PokerCoach.Application.Tournaments;
using PokerCoach.HandHistories;
using PokerCoach.HandHistories.Winamax;
using PokerCoach.Infrastructure;
using PokerCoach.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PokerCoach.IntegrationTests.PostgresFixture))]


namespace PokerCoach.IntegrationTests;

/// <summary>
/// One PostgreSQL container for the whole run, schema created by the real migrations (they are tested
/// too). Tests isolate their data by creating their own user. Where Docker is unavailable, set
/// POKERCOACH_TEST_CONNECTION to an empty database on a running PostgreSQL instead.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string ConnectionVariable = "POKERCOACH_TEST_CONNECTION";

    private PostgreSqlContainer? container;

    private ServiceProvider? services;

    public IServiceProvider Services => services ?? throw new InvalidOperationException("Fixture not initialized.");

    public async ValueTask InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Same image as docker-compose.yml. The parameterless builder is obsolete in recent Testcontainers
            // versions in favour of a constructor taking the image; WithImage works with both.
#pragma warning disable CS0618
            container = new PostgreSqlBuilder().WithImage("postgres:18-alpine").Build();
#pragma warning restore CS0618
            await container.StartAsync();
            connectionString = container.GetConnectionString();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:PokerCoach"] = connectionString })
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
        collection.AddScoped<TournamentListService>();
        collection.AddScoped<PerformanceService>();
        collection.AddScoped<HandFactsBackfill>();
        collection.AddScoped<CoverageBackfill>();
        collection.AddScoped<StatisticsService>();
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

        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }
}
