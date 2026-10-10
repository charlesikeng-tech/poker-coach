using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokerCoach.Application.Identity;

namespace PokerCoach.Infrastructure.Email;

/// <summary>Configuration section "Email". The Brevo key comes from user-secrets or a secret store, never a file.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>A sender verified in Brevo (domain authenticated with SPF/DKIM), e.g. no-reply@your-domain.</summary>
    public string FromAddress { get; set; } = "no-reply@poker-coach.local";

    public string FromName { get; set; } = "NutsIQ";

    public string? BrevoApiKey { get; set; }

    public Uri BrevoBaseUrl { get; set; } = new("https://api.brevo.com/");
}

/// <summary>
/// Brevo transactional email API (EU-hosted). Plain HTTP: one endpoint, no SDK. A failure is logged and
/// thrown: the caller decides (account flows answer the same way either way, so nobody learns more).
/// </summary>
internal sealed partial class BrevoEmailSender(HttpClient http, IOptions<EmailOptions> options, ILogger<BrevoEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.BrevoBaseUrl, "v3/smtp/email"))
        {
            Content = JsonContent.Create(new BrevoEmail(
                new BrevoContact(settings.FromAddress, settings.FromName),
                [new BrevoContact(message.To, null)],
                message.Subject,
                message.Html,
                message.Text)),
        };
        request.Headers.Add("api-key", settings.BrevoApiKey);

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // The body names the problem (unverified sender, quota); it never contains the recipient's data.
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            LogFailure(logger, (int)response.StatusCode, detail.Length > 300 ? detail[..300] : detail);
            throw new InvalidOperationException($"Brevo answered {(int)response.StatusCode}.");
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Email not sent: Brevo answered HTTP {Status}: {Detail}")]
    private static partial void LogFailure(ILogger logger, int status, string detail);

    private sealed record BrevoContact(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name);

    private sealed record BrevoEmail(
        [property: JsonPropertyName("sender")] BrevoContact Sender,
        [property: JsonPropertyName("to")] IReadOnlyList<BrevoContact> To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("htmlContent")] string HtmlContent,
        [property: JsonPropertyName("textContent")] string TextContent);
}

/// <summary>
/// No Brevo key: in Development the email (with its link) goes to the log so sign-up can be tried locally;
/// elsewhere only the fact that nothing was sent is logged, never the link (it would open the account).
/// </summary>
internal sealed partial class LogEmailSender(IHostEnvironment environment, ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (environment.IsDevelopment())
        {
            LogDevelopment(logger, message.To, message.Subject, message.Text);
        }
        else
        {
            LogNotConfigured(logger, message.Subject);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Development email to {To}: {Subject}\n{Text}")]
    private static partial void LogDevelopment(ILogger logger, string to, string subject, string text);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email \"{Subject}\" not sent: Email:BrevoApiKey is not configured.")]
    private static partial void LogNotConfigured(ILogger logger, string subject);
}
