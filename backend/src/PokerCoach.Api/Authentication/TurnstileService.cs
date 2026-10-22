using System.Text.Json.Serialization;

namespace PokerCoach.Api.Authentication;

public sealed class TurnstileOptions
{
    public const string SectionName = "Turnstile";

    public string SecretKey { get; set; } = string.Empty;
}

public sealed class TurnstileService(HttpClient httpClient, TurnstileOptions options)
{
    private const string VerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var payload = new Dictionary<string, string>
        {
            ["secret"] = options.SecretKey,
            ["response"] = token,
        };
        if (!string.IsNullOrWhiteSpace(remoteIp))
            payload["remoteip"] = remoteIp;

        using var response = await httpClient.PostAsync(VerifyUrl, new FormUrlEncodedContent(payload), cancellationToken);
        if (!response.IsSuccessStatusCode)
            return false;

        var result = await response.Content.ReadFromJsonAsync<TurnstileResponse>(cancellationToken);
        return result?.Success == true;
    }

    private sealed class TurnstileResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
    }
}
