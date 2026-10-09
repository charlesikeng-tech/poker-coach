using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PokerCoach.Api.Tests;

public sealed class AuthenticationTests
{
    [Theory]
    [InlineData("GET", "/api/me")]
    [InlineData("PUT", "/api/me/preferences")]
    [InlineData("POST", "/api/auth/logout")]
    [InlineData("POST", "/api/import/files")]
    [InlineData("GET", "/api/import/batches/0199c5a8-0000-7000-8000-000000000000")]
    [InlineData("GET", "/api/poker-accounts")]
    [InlineData("POST", "/api/poker-accounts/0199c5a8-0000-7000-8000-000000000000/confirm")]
    [InlineData("DELETE", "/api/poker-accounts/0199c5a8-0000-7000-8000-000000000000")]
    [InlineData("GET", "/api/tournaments")]
    [InlineData("GET", "/api/performance")]
    [InlineData("GET", "/api/statistics")]
    public async Task Api_endpoints_answer_401_problem_details_to_anonymous_callers(string method, string path)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("UNAUTHENTICATED", await ErrorCodeAsync(response));
    }

    [Theory]
    [InlineData("POST", "/api/auth/logout")]
    [InlineData("PUT", "/api/me/preferences")]
    [InlineData("POST", "/api/import/files")]
    [InlineData("POST", "/api/poker-accounts/0199c5a8-0000-7000-8000-000000000000/confirm")]
    [InlineData("DELETE", "/api/poker-accounts/0199c5a8-0000-7000-8000-000000000000")]
    public async Task Unsafe_requests_without_an_anti_forgery_token_are_rejected(string method, string path)
    {
        await using var factory = new ApiFactory(signedIn: true);

        // HTTPS: outside Development the anti-forgery cookie is Secure-only, and the antiforgery system
        // refuses to run on a plain-HTTP request in that configuration.
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        // Each endpoint gets a body it accepts: the upload declares multipart/form-data only, and any
        // other media type is refused (415) by routing before the anti-forgery filter runs.
        HttpContent content = path == "/api/import/files"
            ? new MultipartFormDataContent { { new ByteArrayContent("hands"u8.ToArray()), "files", "hands.txt" } }
            : JsonContent.Create(new { language = "fr" });
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative)) { Content = content };
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_ANTIFORGERY_TOKEN", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Unknown_routes_are_404_even_for_anonymous_callers()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/nothing-here", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NOT_FOUND", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Sign_in_starts_a_google_authorization_code_flow_with_pkce()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(
            new Uri("/api/auth/login?returnUrl=%2Fperformance&language=fr", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);
        Assert.Equal("accounts.google.com", location.Host);
        Assert.Contains("response_type=code", location.Query, StringComparison.Ordinal);
        Assert.Contains("code_challenge=", location.Query, StringComparison.Ordinal);
        Assert.Contains("client_id=test-client-id", location.Query, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("http://localhost/signin-google"), location.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    public async Task Sign_in_refuses_to_redirect_outside_the_site(string returnUrl)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(
            new Uri($"/api/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Startup_fails_fast_when_the_google_client_is_not_configured()
    {
        await using var factory = new ApiFactory(googleClientId: string.Empty);

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("Authentication:Google:ClientId", exception.ToString(), StringComparison.Ordinal);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return body.RootElement.GetProperty("code").GetString();
    }
}
