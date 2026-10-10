using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PokerCoach.Application.Bankroll;
using PokerCoach.Application.Progress;
using PokerCoach.Application.Tournaments;

namespace PokerCoach.Application.Identity;

public sealed record ExportedIdentity(string Provider, string Subject, DateTimeOffset CreatedAt);

public sealed record ExportedPokerAccount(string Room, string ScreenName, DateTimeOffset CreatedAt, DateTimeOffset? ConfirmedAt);

public sealed record ExportedDrillAttempt(DateTimeOffset AnsweredAt, string Format, string Band, string Position, string? Shover, int? PushStack, string Hand, string Answer, bool Correct);

/// <param name="Payload">The explanation as the model wrote it (JSON).</param>
public sealed record ExportedExplanation(DateTimeOffset CreatedAt, string Language, string Model, string Payload);

/// <param name="Payload">The debrief or review as stored (JSON).</param>
public sealed record ExportedReport(string Kind, string Subject, DateTimeOffset CreatedAt, string Language, string Model, string Payload);

/// <summary>Everything stored about a user except tournaments and uploaded files, which are exported apart.</summary>
public sealed record AccountSnapshot(
    string DisplayName,
    string? Email,
    string PreferredLanguage,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ExportedIdentity> Identities,
    IReadOnlyList<ExportedPokerAccount> PokerAccounts,
    IReadOnlyList<ExportedDrillAttempt> DrillAttempts,
    IReadOnlyList<ExportedExplanation> Explanations,
    IReadOnlyList<ExportedReport>? Reports = null);

/// <summary>One uploaded file as stored (gzip), with its original name.</summary>
public sealed record ExportedUpload(string FileName, DateTimeOffset UploadedAt, byte[] ContentGzip);

public interface IAccountDataStore
{
    Task<AccountSnapshot?> GetSnapshotAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The user's uploaded files, one at a time: an export must not hold them all in memory.</summary>
    IAsyncEnumerable<ExportedUpload> StreamUploadsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Deletes the user; the database cascades to everything they own.</summary>
    /// <returns>False when the user did not exist.</returns>
    Task<bool> DeleteUserAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// The player's rights over their data (GDPR articles 15, 17 and 20): a complete, readable export, and the
/// deletion of the account with everything it owns. The export holds the files exactly as uploaded (the
/// source of everything else), the tournaments with their results as a spreadsheet, and the rest as JSON.
/// </summary>
public sealed class AccountDataService(
    IAccountDataStore store,
    TournamentListService tournaments,
    IBankrollStore bankroll,
    IWeeklyPlanStore plans,
    TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <param name="destination">Written synchronously (zip entries): a file, not a network stream.</param>
    /// <returns>False when the user does not exist.</returns>
    public async Task<bool> WriteExportAsync(Guid userId, Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var snapshot = await store.GetSnapshotAsync(userId, cancellationToken);
        if (snapshot is null)
        {
            return false;
        }

        var settings = await bankroll.GetSettingsAsync(userId, cancellationToken);
        var movements = await bankroll.ListMovementsAsync(userId, cancellationToken);
        var weeklyPlans = await plans.ListBeforeAsync(userId, DateOnly.MaxValue, int.MaxValue, cancellationToken);
        var page = await tournaments.ListAsync(userId, new TournamentFilter(null, null, null, null, 1, int.MaxValue), cancellationToken);

        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        WriteText(zip, "README.txt", Readme(time.GetUtcNow()));
        WriteText(zip, "account.json", JsonSerializer.Serialize(
            new
            {
                ExportedAt = time.GetUtcNow(),
                Account = new { snapshot.DisplayName, snapshot.Email, snapshot.PreferredLanguage, snapshot.CreatedAt },
                snapshot.Identities,
                snapshot.PokerAccounts,
                Bankroll = new { Settings = settings, Movements = movements },
                WeeklyPlans = weeklyPlans,
                snapshot.DrillAttempts,
                CoachExplanations = snapshot.Explanations.Select(e => new { e.CreatedAt, e.Language, e.Model, Content = JsonDocument.Parse(e.Payload).RootElement }),
                CoachReports = (snapshot.Reports ?? []).Select(r => new { r.Kind, r.Subject, r.CreatedAt, r.Language, r.Model, Content = JsonDocument.Parse(r.Payload).RootElement }),
            },
            Json));
        WriteText(zip, "tournaments.csv", TournamentsCsv(page.Items));

        var index = 0;
        await foreach (var upload in store.StreamUploadsAsync(userId, cancellationToken))
        {
            index++;
            var entry = zip.CreateEntry(
                $"uploads/{upload.UploadedAt.UtcDateTime:yyyyMMdd-HHmmss}-{index:D4}-{SafeName(upload.FileName)}",
                CompressionLevel.Optimal);
            using var target = entry.Open();
            using var source = new GZipStream(new MemoryStream(upload.ContentGzip), CompressionMode.Decompress);
            await source.CopyToAsync(target, cancellationToken);
        }

        return true;
    }

    public Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
        store.DeleteUserAsync(userId, cancellationToken);

    internal static string TournamentsCsv(IEnumerable<TournamentListItem> items)
    {
        var csv = new StringBuilder();
        csv.AppendLine("started_at,name,currency,buy_in,registered_players,finish_position,hands,result_status,entries,total_buy_in,prize_winnings,bounty_winnings,profit");
        foreach (var t in items.OrderBy(t => t.StartedAt))
        {
            csv.AppendJoin(
                ',',
                Field(t.StartedAt?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
                Field(t.Name),
                Field(t.Currency),
                Field(t.BuyIn),
                Field(t.RegisteredPlayers),
                Field(t.FinishPosition),
                Field(t.HandCount),
                Field(t.Result.Status.ToString()),
                Field(t.Result.Entries),
                Field(t.Result.TotalBuyIn),
                Field(t.Result.PrizeWinnings),
                Field(t.Result.BountyWinnings),
                Field(t.Result.Profit));
            csv.AppendLine();
        }

        return csv.ToString();
    }

    /// <summary>RFC 4180: quote when needed, double the quotes. Unknown values stay empty, never 0.</summary>
    private static string Field(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
        return text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : text;
    }

    /// <summary>Only the file name, no folder from the upload: nothing can escape the archive's folder.</summary>
    private static string SafeName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "file.txt" : safe;
    }

    private static void WriteText(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string Readme(DateTimeOffset now) => $"""
        NutsIQ — export of your data ({now.UtcDateTime:yyyy-MM-dd HH:mm} UTC)

        uploads/          The files you uploaded, exactly as you sent them (hand histories, summaries).
                          Everything else in NutsIQ was computed from them.
        tournaments.csv   Your tournaments and their results. Empty cells mean unknown (a missing
                          summary file), never zero.
        account.json      Your account, linked sign-in, poker accounts, bankroll, weekly plans,
                          training answers and the coach's explanations.

        Statistics, leaks and ranges are not included: they are recomputed from your hands.
        """;
}
