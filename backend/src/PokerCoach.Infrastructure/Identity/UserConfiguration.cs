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

internal sealed class PasswordCredentialConfiguration : IEntityTypeConfiguration<PasswordCredential>
{
    public void Configure(EntityTypeBuilder<PasswordCredential> builder)
    {
        builder.ToTable("password_credentials", IdentitySchema.Name);
        builder.HasKey(c => c.UserId);
        builder.Property(c => c.Email).HasMaxLength(EmailAddresses.MaxLength).IsRequired();
        builder.Property(c => c.PasswordHash).HasMaxLength(512).IsRequired();

        // One login per email, enforced by the database: concurrent sign-ups end with one account.
        builder.HasIndex(c => c.Email).IsUnique();
        builder.HasOne<User>().WithOne().HasForeignKey<PasswordCredential>(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EmailTokenConfiguration : IEntityTypeConfiguration<EmailToken>
{
    public void Configure(EntityTypeBuilder<EmailToken> builder)
    {
        builder.ToTable("email_tokens", IdentitySchema.Name);
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Purpose).HasConversion<int>();
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // The per-hour email limit counts a user's recent tokens of one purpose.
        builder.HasIndex(t => new { t.UserId, t.Purpose, t.CreatedAt });
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
