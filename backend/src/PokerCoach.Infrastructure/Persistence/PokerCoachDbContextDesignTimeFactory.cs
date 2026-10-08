using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace PokerCoach.Infrastructure.Persistence;

/// <summary>
/// Used by `dotnet ef` only. Creates the DbContext without starting the API host, so migrations do not
/// depend on the API's environment or startup checks (Google client, fail-fast options).
/// Connection string: the API's user secrets, or the ConnectionStrings__PokerCoach environment variable.
/// </summary>
public sealed class PokerCoachDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PokerCoachDbContext>
{
    // Same value as <UserSecretsId> in PokerCoach.Api.csproj.
    private const string ApiUserSecretsId = "poker-coach-api";

    // `migrations add` never connects, so a placeholder is enough there; `database update` needs the real one.
    private const string PlaceholderConnectionString = "Host=localhost;Port=5432;Database=poker_coach";

    public PokerCoachDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(ApiUserSecretsId)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString(DatabaseOptions.ConnectionStringName);
        var options = new DbContextOptionsBuilder<PokerCoachDbContext>()
            .UseNpgsql(
                string.IsNullOrWhiteSpace(connectionString) ? PlaceholderConnectionString : connectionString,
                npgsql => npgsql.MigrationsHistoryTable(DatabaseOptions.MigrationsHistoryTable, "public"))
            .Options;

        return new PokerCoachDbContext(options);
    }
}
