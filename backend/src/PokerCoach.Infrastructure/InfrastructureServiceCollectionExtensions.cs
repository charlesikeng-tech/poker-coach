using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Configure(o => o.ConnectionString = configuration.GetConnectionString(DatabaseOptions.ConnectionStringName) ?? string.Empty)
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.ConnectionString),
                $"Missing connection string 'ConnectionStrings:{DatabaseOptions.ConnectionStringName}'. "
                + "Set it with `dotnet user-secrets` or the ConnectionStrings__PokerCoach environment variable.")
            .ValidateOnStart();

        services.AddDbContext<PokerCoachDbContext>((sp, options) =>
        {
            var database = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            options.UseNpgsql(database.ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "public"));
        });

        services.AddHealthChecks()
            .AddDbContextCheck<PokerCoachDbContext>("database", tags: [HealthCheckTags.Ready]);

        return services;
    }
}
