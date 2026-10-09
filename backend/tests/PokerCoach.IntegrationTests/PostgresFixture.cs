using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PokerCoach.Application.Identity;
using PokerCoach.Application.Import;
using PokerCoach.Application.Poker;
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
/// too). Tests isolate their data by creating their own user.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Same image as docker-compose.yml. The parameterless builder is obsolete in recent Testcontainers
    // versions in favour of a constructor taking the image; WithImage works with both.
#pragma warning disable CS0618
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder()
        .WithImage("postgres:18-alpine")
        .Build();
#pragma warning restore CS0618

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
        collection.AddScoped<TournamentListService>();
        collection.AddScoped<PerformanceService>();
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
