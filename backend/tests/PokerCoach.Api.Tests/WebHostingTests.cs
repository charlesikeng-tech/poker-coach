using System.Net;
using Microsoft.AspNetCore.Http;
using PokerCoach.Api.Analytics;

namespace PokerCoach.Api.Tests;

public sealed class WebHostingTests : IDisposable
{
    private readonly string webRoot = Directory.CreateTempSubdirectory("poker-coach-web-").FullName;

    public WebHostingTests()
    {
        File.WriteAllText(Path.Combine(webRoot, "index.html"), "<app-root></app-root>");
        File.WriteAllText(Path.Combine(webRoot, "main-ABCD1234.js"), "console.log(1)");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_response_carries_the_security_headers()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/me", UriKind.Relative), Ct);

        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self';", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/tournaments/0199c5a8-0000-7000-8000-000000000000")]
    [InlineData("/sign-in")]
    public async Task Client_side_routes_answer_the_web_app_to_anonymous_visitors(string path)
    {
        await using var factory = new ApiFactory(webRoot: webRoot);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("<app-root></app-root>", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Hashed_bundles_are_cached_for_a_year()
    {
        await using var factory = new ApiFactory(webRoot: webRoot);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/main-ABCD1234.js", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("/api/does-not-exist")]
    [InlineData("/missing-ABCD1234.js")]
    public async Task Unknown_api_routes_and_missing_files_stay_404_with_the_web_app_served(string path)
    {
        await using var factory = new ApiFactory(webRoot: webRoot);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/statistics", "statistics")]
    [InlineData("/api/statistics/breakdowns", "statistics")]
    [InlineData("/api/training/opening/spot", "training")]
    [InlineData("/api/auth/login", null)]
    [InlineData("/api", null)]
    [InlineData("/health/ready", null)]
    [InlineData("/api/Weird_Segment", null)]
    public void Usage_is_counted_by_the_first_api_segment(string path, string? feature) =>
        Assert.Equal(feature, FeatureUsageTracker.FeatureOf(new PathString(path)));

    public void Dispose() => Directory.Delete(webRoot, recursive: true);
}
