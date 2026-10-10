using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PokerCoach.Api.Tests;

public sealed class LocalAccountEndpointsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Anonymous_visitors_get_an_anti_forgery_token_for_the_sign_in_forms()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });

        var response = await client.GetAsync(new Uri("/api/me", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/api/auth/register")]
    [InlineData("/api/auth/password/login")]
    [InlineData("/api/auth/email/confirm")]
    [InlineData("/api/auth/email/resend")]
    [InlineData("/api/auth/password/forgot")]
    [InlineData("/api/auth/password/reset")]
    public async Task Account_forms_without_an_anti_forgery_token_are_rejected(string path)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var response = await client.PostAsJsonAsync(new Uri(path, UriKind.Relative), new { email = "jo@example.com", password = "x" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("INVALID_ANTIFORGERY_TOKEN", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Startup_fails_outside_development_without_a_public_url_for_emailed_links()
    {
        await using var factory = new ApiFactory(publicUrl: null);

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("App:PublicUrl", exception.ToString(), StringComparison.Ordinal);
    }
}
