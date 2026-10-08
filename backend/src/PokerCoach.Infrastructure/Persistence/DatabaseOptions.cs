namespace PokerCoach.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    /// <summary>Configuration key: <c>ConnectionStrings:PokerCoach</c> (env: <c>ConnectionStrings__PokerCoach</c>).</summary>
    public const string ConnectionStringName = "PokerCoach";

    internal const string MigrationsHistoryTable = "__ef_migrations_history";

    public string ConnectionString { get; set; } = string.Empty;
}
