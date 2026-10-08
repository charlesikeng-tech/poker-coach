using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerCoach.Domain.Identity;

namespace PokerCoach.Infrastructure.Identity;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", IdentitySchema.Name);
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(320);
        builder.Property(u => u.PreferredLanguage).HasMaxLength(8).IsRequired();
        builder.Property(u => u.Status).HasConversion<int>();
        builder.Property(u => u.CreatedAt).IsRequired();
        builder.Property(u => u.LastLoginAt).IsRequired();
    }
}

internal sealed class ExternalIdentityConfiguration : IEntityTypeConfiguration<ExternalIdentity>
{
    public void Configure(EntityTypeBuilder<ExternalIdentity> builder)
    {
        builder.ToTable("external_identities", IdentitySchema.Name);
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Provider).HasMaxLength(32).IsRequired();
        builder.Property(i => i.ProviderSubjectId).HasMaxLength(255).IsRequired();
        builder.Property(i => i.Email).HasMaxLength(320);

        // The identity rule of the product: one account per (provider, subject). Concurrency safety of
        // the first sign-in relies on this constraint, not on application checks.
        builder.HasIndex(i => new { i.Provider, i.ProviderSubjectId }).IsUnique();

        builder.HasOne<User>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal static class IdentitySchema
{
    public const string Name = "identity";
}
