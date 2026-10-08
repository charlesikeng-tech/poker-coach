using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Import;

namespace PokerCoach.Api.Import;

public sealed record UploadedFileResponse(Guid FileId, string FileName, ImportFileStatus Status, bool AlreadyImported);

public sealed record RejectedFileResponse(string FileName, string Code);

/// <param name="Files">Files stored for processing, or already imported before (same content).</param>
/// <param name="Rejected">Files refused before processing (type, size, archive); <c>code</c> is one of the import error codes.</param>
public sealed record UploadResponse(Guid BatchId, IReadOnlyList<UploadedFileResponse> Files, IReadOnlyList<RejectedFileResponse> Rejected);

public sealed record ImportedFileResponse(
    Guid FileId,
    string FileName,
    ImportFileStatus Status,
    ImportFileKind? Kind,
    string? ErrorCode,
    string? ScreenName,
    int? HandsImported,
    int? HandsAlreadyPresent,
    int? HandsRejected,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

/// <param name="IsComplete">True when every file is completed or failed: the client stops polling.</param>
public sealed record BatchResponse(Guid BatchId, bool IsComplete, IReadOnlyList<ImportedFileResponse> Files);

public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var limits = endpoints.ServiceProvider.GetRequiredService<ImportOptions>();
        var group = endpoints.MapGroup("/api/import").WithTags("Import");

        group.MapPost("/files", UploadAsync)
            .AddEndpointFilter<AntiforgeryValidationFilter>()
            .WithMetadata(new RequestSizeLimitAttribute(limits.MaxUploadBytes))
            .WithFormOptions(multipartBodyLengthLimit: limits.MaxUploadBytes)
            .Accepts<IFormFileCollection>("multipart/form-data");

        group.MapGet("/batches/{batchId:guid}", GetBatchAsync);

        return endpoints;
    }

    /// <summary>
    /// Stores the files and returns at once (202 with the batch to poll); parsing happens in the background.
    /// Answers 200 when nothing new had to be processed (all files rejected or already imported).
    /// </summary>
    private static async Task<Results<Accepted<UploadResponse>, Ok<UploadResponse>, ProblemHttpResult, UnauthorizedHttpResult>> UploadAsync(
        HttpContext context,
        ImportService imports,
        ImportOptions limits,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (!context.Request.HasFormContentType)
        {
            return ApiProblems.Validation("files", "Expected a multipart/form-data body with one or more files.");
        }

        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(cancellationToken);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return ApiProblems.WithCode(
                StatusCodes.Status413PayloadTooLarge,
                ImportErrorCodes.UploadTooLarge,
                "Upload too large.",
                new Dictionary<string, object?> { ["maxBytes"] = limits.MaxUploadBytes });
        }
        catch (InvalidDataException)
        {
            return ApiProblems.Validation("files", "Malformed multipart body.");
        }

        if (form.Files.Count == 0)
        {
            return ApiProblems.Validation("files", "No file in the upload.");
        }

        var sources = form.Files.Select(file => new UploadSource(file.FileName, file.OpenReadStream)).ToList();
        var result = await imports.UploadAsync(userId, sources, cancellationToken);
        if (result.TooManyFiles)
        {
            return ApiProblems.WithCode(
                StatusCodes.Status400BadRequest,
                ImportErrorCodes.TooManyFiles,
                "Too many files in one upload.",
                new Dictionary<string, object?> { ["maxFiles"] = limits.MaxFilesPerUpload });
        }

        var response = new UploadResponse(
            result.BatchId,
            result.Files.Select(f => new UploadedFileResponse(f.FileId, f.FileName, f.Status, f.AlreadyImported)).ToList(),
            result.Rejected.Select(r => new RejectedFileResponse(r.FileName, r.Code)).ToList());

        return result.HasQueuedFiles
            ? TypedResults.Accepted($"/api/import/batches/{result.BatchId}", response)
            : TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<BatchResponse>, NotFound, UnauthorizedHttpResult>> GetBatchAsync(
        Guid batchId,
        HttpContext context,
        ImportService imports,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        // Another user's batch is indistinguishable from a missing one.
        var files = await imports.GetBatchAsync(userId, batchId, cancellationToken);
        if (files.Count == 0)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new BatchResponse(
            batchId,
            files.All(f => f.Status is ImportFileStatus.Completed or ImportFileStatus.Failed),
            files.Select(f => new ImportedFileResponse(
                f.FileId,
                f.FileName,
                f.Status,
                f.Kind,
                f.ErrorCode,
                f.ScreenName,
                f.HandsImported,
                f.HandsAlreadyPresent,
                f.HandsRejected,
                f.CreatedAt,
                f.CompletedAt)).ToList()));
    }
}
