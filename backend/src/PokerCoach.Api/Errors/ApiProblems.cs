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
}
