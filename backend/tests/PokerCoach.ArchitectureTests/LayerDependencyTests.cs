using System.Reflection;
using PokerCoach.Application;
using PokerCoach.Domain;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.ArchitectureTests;

/// <summary>
/// Guards the dependency rule of ADR-0001 at assembly level. Project references already prevent most
/// violations; these tests catch the ones a well-meaning package reference would sneak in.
/// </summary>
public sealed class LayerDependencyTests
{
    private static readonly string[] ForbiddenInApplication =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "PokerCoach.Infrastructure",
        "PokerCoach.Api",
    ];

    [Fact]
    public void Domain_references_only_the_base_class_library()
    {
        var offending = ReferencedAssemblyNames(typeof(DomainAssembly).Assembly)
            .Where(name => !IsBaseClassLibrary(name))
            .ToArray();

        Assert.Empty(offending);
    }

    [Fact]
    public void Application_does_not_reference_infrastructure_or_web_frameworks()
    {
        var offending = ReferencedAssemblyNames(typeof(ApplicationAssembly).Assembly)
            .Where(name => ForbiddenInApplication.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(offending);
    }

    [Fact]
    public void Infrastructure_does_not_reference_the_api()
    {
        var offending = ReferencedAssemblyNames(typeof(PokerCoachDbContext).Assembly)
            .Where(name => name.StartsWith("PokerCoach.Api", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(offending);
    }

    private static IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty);

    private static bool IsBaseClassLibrary(string name) =>
        name.StartsWith("System", StringComparison.Ordinal)
        || name is "netstandard" or "mscorlib";
}
