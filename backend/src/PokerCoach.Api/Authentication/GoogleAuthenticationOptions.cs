namespace PokerCoach.Api.Authentication;

/// <summary>Google OAuth client ("Web application" in Google Cloud Console). Secrets only: never in appsettings.</summary>
public sealed class GoogleAuthenticationOptions
{
    public const string SectionName = "Authentication:Google";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;
}
