using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PokerCoach.Application.Analytics;
using PokerCoach.Application.Bankroll;
using PokerCoach.Application.Coaching;
using PokerCoach.Application.Hands;
using PokerCoach.Application.Identity;
using PokerCoach.Application.Import;
using PokerCoach.Application.Poker;
using PokerCoach.Application.Progress;
using PokerCoach.Application.Ranges;
using PokerCoach.Application.Statistics;
using PokerCoach.Application.Tournaments;
using PokerCoach.Application.Training;
using PokerCoach.Infrastructure.Bankroll;
using PokerCoach.Infrastructure.Coaching;
using PokerCoach.Infrastructure.Hands;
using PokerCoach.Infrastructure.Identity;
using PokerCoach.Infrastructure.Import;
using PokerCoach.Infrastructure.Persistence;
using PokerCoach.Infrastructure.Platform;
using PokerCoach.Infrastructure.Poker;
using PokerCoach.Infrastructure.Progress;
using PokerCoach.Infrastructure.Statistics;
using PokerCoach.Infrastructure.Tournaments;
using PokerCoach.Infrastructure.Training;

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
                npgsql.MigrationsHistoryTable(DatabaseOptions.MigrationsHistoryTable, "public"));
        });

        services.AddScoped<IUserAccountStore, UserAccountStore>();
        services.AddScoped<IAccountDataStore, AccountDataStore>();
        services.AddScoped<IImportStore, ImportStore>();
        services.AddScoped<IPokerAccountStore, PokerAccountStore>();
        services.AddScoped<IHandReplayStore, HandReplayStore>();
        services.AddScoped<ITrainingStore, TrainingStore>();
        services.AddScoped<IBankrollStore, BankrollStore>();
        services.AddScoped<IWeeklyPlanStore, WeeklyPlanStore>();
        services.AddScoped<TournamentReadStore>();
        services.AddScoped<ITournamentReadStore>(sp => sp.GetRequiredService<TournamentReadStore>());
        services.AddScoped<ITournamentDetailStore>(sp => sp.GetRequiredService<TournamentReadStore>());
        services.AddScoped<ICoverageStore, CoverageStore>();
        services.AddScoped<IHandFactsStore, HandFactsStore>();
        services.AddScoped<StatisticsReadStore>();
        services.AddScoped<IStatisticsReadStore>(sp => sp.GetRequiredService<StatisticsReadStore>());
        services.AddScoped<IRangeReadStore>(sp => sp.GetRequiredService<StatisticsReadStore>());
        services.AddScoped<IAllInReadStore>(sp => sp.GetRequiredService<StatisticsReadStore>());
        services.AddScoped<IRealSpotStore>(sp => sp.GetRequiredService<StatisticsReadStore>());
        services.AddScoped<ICoachingStore, CoachingStore>();
        services.AddScoped<ICoachingReportStore, CoachingReportStore>();
        services.AddScoped<IFeatureUsageStore, FeatureUsageStore>();

        // No API key = coaching unavailable, not a startup failure (ADR-0008).
        services.AddOptions<AnthropicOptions>().BindConfiguration(AnthropicOptions.SectionName);
        services.AddHttpClient<ICoachingModel, AnthropicCoachingModel>(client => client.Timeout = TimeSpan.FromSeconds(90));

        // Starts only when Import:WorkerEnabled is true (default); needs ImportProcessor and ImportOptions
        // registered by the host.
        services.AddHostedService<ImportWorker>();

        // Session cookie and anti-forgery keys survive restarts and are shared by every instance (ADR-0011).
        services.AddDataProtection().SetApplicationName("poker-coach");
        services.AddOptions<KeyManagementOptions>()
            .Configure<IServiceScopeFactory>((options, scopes) => options.XmlRepository = new DataProtectionKeyStore(scopes));

        services.AddHealthChecks()
            .AddDbContextCheck<PokerCoachDbContext>("database", tags: [HealthCheckTags.Ready]);

        return services;
    }
}
