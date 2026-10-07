namespace PokerCoach.Api.Errors;

/// <summary>
/// Machine-readable error codes returned in the <c>code</c> member of every problem response.
/// Clients branch on these, never on <c>title</c> or <c>detail</c>. Values are part of the public contract:
/// add new ones freely, never rename or reuse existing ones.
/// </summary>
public static class ApiErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
    public const string Conflict = "CONFLICT";
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string RateLimited = "RATE_LIMITED";
    public const string InternalError = "INTERNAL_ERROR";
    public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
    public const string Unknown = "UNKNOWN_ERROR";

    public static string FromStatusCode(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => ValidationFailed,
        StatusCodes.Status401Unauthorized => Unauthenticated,
        StatusCodes.Status403Forbidden => Forbidden,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowed,
        StatusCodes.Status409Conflict => Conflict,
        StatusCodes.Status413PayloadTooLarge => PayloadTooLarge,
        StatusCodes.Status429TooManyRequests => RateLimited,
        StatusCodes.Status500InternalServerError => InternalError,
        StatusCodes.Status503ServiceUnavailable => ServiceUnavailable,
        _ => Unknown,
    };
}
