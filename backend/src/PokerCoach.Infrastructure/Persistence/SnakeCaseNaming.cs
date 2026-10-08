using System.Text;
using Microsoft.EntityFrameworkCore;

namespace PokerCoach.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL naming: snake_case columns, keys, foreign keys and indexes, so that hand-written SQL never
/// needs quoted identifiers. Table names and schemas are set explicitly in each configuration.
/// Kept in-house (a few lines) rather than adding a package for it.
/// </summary>
internal static class SnakeCaseNaming
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            var table = entity.GetTableName();
            foreach (var key in entity.GetKeys())
            {
                key.SetName(key.IsPrimaryKey()
                    ? $"pk_{table}"
                    : $"ak_{table}_{string.Join('_', key.Properties.Select(p => ToSnakeCase(p.Name)))}");
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(
                    $"fk_{table}_{foreignKey.PrincipalEntityType.GetTableName()}_{string.Join('_', foreignKey.Properties.Select(p => ToSnakeCase(p.Name)))}");
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(
                    $"{(index.IsUnique ? "ux" : "ix")}_{table}_{string.Join('_', index.Properties.Select(p => ToSnakeCase(p.Name)))}");
            }
        }
    }

    internal static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                // Word boundary: "UserId" -> user_id, "HTTPCode" -> http_code.
                var previousIsLower = i > 0 && char.IsLower(name[i - 1]);
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (i > 0 && (previousIsLower || (nextIsLower && char.IsUpper(name[i - 1]))))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
