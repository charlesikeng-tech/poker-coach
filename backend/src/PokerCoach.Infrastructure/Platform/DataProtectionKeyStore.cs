using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.Infrastructure.Platform;

internal static class PlatformSchema
{
    public const string Name = "platform";
}

/// <summary>One ASP.NET Core data-protection key (XML, as the framework writes it).</summary>
internal sealed class DataProtectionKeyRecord
{
    public int Id { get; set; }

    /// <summary>The framework's own name for the element ("key-{guid}"); unique.</summary>
    public string FriendlyName { get; set; } = string.Empty;

    public string Xml { get; set; } = string.Empty;
}

internal sealed class DataProtectionKeyConfiguration : IEntityTypeConfiguration<DataProtectionKeyRecord>
{
    public void Configure(EntityTypeBuilder<DataProtectionKeyRecord> builder)
    {
        builder.ToTable("data_protection_keys", PlatformSchema.Name);
        builder.HasKey(k => k.Id);
        builder.HasIndex(k => k.FriendlyName).IsUnique();
    }
}

/// <summary>
/// Keeps the keys that sign the session cookie and anti-forgery tokens in the database, so a redeploy or a
/// second instance does not sign everyone out (the default is the container's file system, lost on every
/// restart). Written by hand rather than with Microsoft.AspNetCore.DataProtection.EntityFrameworkCore: the
/// same few lines, on our own DbContext and naming conventions. Keys are stored unencrypted (ADR-0011):
/// whoever can read this table can already read every user's data.
/// </summary>
/// <remarks>
/// A singleton (the framework holds the repository for the app's lifetime), so each call opens its own
/// scope. Calls are rare: keys are read at startup and roughly every 24 hours, written every 90 days.
/// </remarks>
internal sealed class DataProtectionKeyStore(IServiceScopeFactory scopes) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokerCoachDbContext>();
        return db.Set<DataProtectionKeyRecord>()
            .AsNoTracking()
            .OrderBy(k => k.Id)
            .Select(k => k.Xml)
            .AsEnumerable()
            .Select(xml => XElement.Parse(xml))
            .ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokerCoachDbContext>();

        // Two instances starting together may both create a key: keep the first, both stay valid readers.
        db.Database.ExecuteSql($"""
            INSERT INTO platform.data_protection_keys (friendly_name, xml)
            VALUES ({friendlyName}, {element.ToString(SaveOptions.DisableFormatting)})
            ON CONFLICT (friendly_name) DO NOTHING
            """);
    }
}
