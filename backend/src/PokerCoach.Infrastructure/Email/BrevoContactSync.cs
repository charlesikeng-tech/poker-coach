using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokerCoach.Application.Notifications;

namespace PokerCoach.Infrastructure.Email;

internal sealed partial class BrevoContactSync(HttpClient http, IOptions<EmailOptions> options, ILogger<BrevoContactSync> logger) : IContactSync
{
    public async Task SyncAsync(ContactAttributes contact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contact);
        var settings = options.Value;

        var attributes = new Dictionary<string, string>();
        if (contact.Plan is not null)
            attributes["PLAN"] = contact.Plan;
        if (contact.SignupDate is { } signup)
            attributes["SIGNUP_DATE"] = signup.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (contact.Language is not null)
            attributes["LANGUAGE"] = contact.Language;
        if (contact.TotalHands is { } hands)
            attributes["TOTAL_HANDS"] = hands.ToString(CultureInfo.InvariantCulture);
        if (contact.Rooms is not null)
            attributes["ROOMS"] = contact.Rooms;
        if (contact.MainGameType is not null)
            attributes["MAIN_GAME_TYPE"] = contact.MainGameType;

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.BrevoBaseUrl, "v3/contacts"))
        {
            Content = JsonContent.Create(new BrevoContactPayload(contact.Email, attributes, UpdateEnabled: true)),
        };
        request.Headers.Add("api-key", settings.BrevoApiKey);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                LogFailure(logger, (int)response.StatusCode, detail.Length > 300 ? detail[..300] : detail);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogException(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Brevo contact sync failed: HTTP {Status}: {Detail}")]
    private static partial void LogFailure(ILogger logger, int status, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Brevo contact sync failed with exception.")]
    private static partial void LogException(ILogger logger, Exception ex);

    private sealed record BrevoContactPayload(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("attributes")] Dictionary<string, string> Attributes,
        [property: JsonPropertyName("updateEnabled")] bool UpdateEnabled);
}

internal sealed partial class LogContactSync(ILogger<LogContactSync> logger) : IContactSync
{
    public Task SyncAsync(ContactAttributes contact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contact);
        LogSync(logger, contact.Email, contact.Plan ?? "-");
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Contact sync (no-op): {Email}, plan={Plan}")]
    private static partial void LogSync(ILogger logger, string email, string plan);
}
