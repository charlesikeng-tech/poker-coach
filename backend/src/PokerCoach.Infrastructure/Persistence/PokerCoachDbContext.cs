using Microsoft.EntityFrameworkCore;

namespace PokerCoach.Infrastructure.Persistence;

/// <summary>
/// Single DbContext for the modular monolith (ADR-0001). Each module maps its tables into its own
/// PostgreSQL schema through <see cref="IEntityTypeConfiguration{TEntity}"/> classes in this assembly.
/// </summary>
public sealed class PokerCoachDbContext(DbContextOptions<PokerCoachDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PokerCoachDbContext).Assembly);
        SnakeCaseNaming.Apply(modelBuilder);
    }
}
