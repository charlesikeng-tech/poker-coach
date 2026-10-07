using System.Net;
using System.Text.Json;

namespace PokerCoach.Api.Tests;

public sealed class HttpContractTests
{
    [Fact]
    public async Task Liveness_does_not_depend_on_the_database()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Errors_are_problem_details_with_a_machine_readable_code_and_trace_id()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("NOT_FOUND", body.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Startup_fails_fast_when_the_connection_string_is_missing()
    {
        await using var factory = new ApiFactory(connectionString: string.Empty);

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("ConnectionStrings:PokerCoach", exception.ToString(), StringComparison.Ordinal);
    }
}
