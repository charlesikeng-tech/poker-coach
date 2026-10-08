using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace PokerCoach.Api.Tests;

/// <summary>
/// In-process API host. The connection string points at a database that is never opened: these tests
/// cover HTTP behaviour that must not depend on PostgreSQL. Database behaviour is covered by
/// Testcontainers-based integration tests (added with the first persisted module).
/// </summary>
internal sealed class ApiFactory(
    string? connectionString = "Host=localhost;Database=unused",
    string googleClientId = "test-client-id",
    bool signedIn = false) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:PokerCoach"] = connectionString,
                ["Authentication:Google:ClientId"] = googleClientId,
                ["Authentication:Google:ClientSecret"] = "test-client-secret",
            }));

        if (signedIn)
        {
            builder.ConfigureTestServices(services => services
                .AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { }));
        }
    }
}
