using Microsoft.AspNetCore.Http.HttpResults;

namespace PokerCoach.Api.Errors;

public static class ApiProblems
{
    /// <summary>400 VALIDATION_FAILED with the offending field, machine-readable.</summary>
    public static ProblemHttpResult Validation(string field, string message) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = ApiErrorCodes.ValidationFailed,
                ["errors"] = new Dictionary<string, string[]> { [field] = [message] },
            });

    /// <summary>A problem with a code more precise than the status code's default one.</summary>
    public static ProblemHttpResult WithCode(int statusCode, string code, string title, IDictionary<string, object?>? details = null)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        if (details is not null)
        {
            extensions["details"] = details;
        }

        return TypedResults.Problem(statusCode: statusCode, title: title, extensions: extensions);
    }
}
