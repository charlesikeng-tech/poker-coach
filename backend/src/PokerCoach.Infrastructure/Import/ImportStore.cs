using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using PokerCoach.Application.Import;
using PokerCoach.Domain.Poker;
using PokerCoach.HandHistories;
using PokerCoach.Infrastructure.Persistence;
using PokerCoach.Infrastructure.Poker;

namespace PokerCoach.Infrastructure.Import;

/// <summary>
/// Writes use hand-written SQL: insert-or-get (<c>ON CONFLICT</c>) and queue claims
/// (<c>FOR UPDATE SKIP LOCKED</c>) have no EF Core equivalent, and they are what makes the import
/// idempotent and safe with several workers. Reads stay LINQ projections. Scoped: one instance per
/// processed file or HTTP request.
/// </summary>
internal sealed class ImportStore(PokerCoachDbContext db, TimeProvider time) : IImportStore
{
    /// <summary>A worker that holds a file longer than this is presumed dead and the file is claimed again.</summary>
    internal static readonly TimeSpan ClaimDuration = TimeSpan.FromMinutes(10);

    internal static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private NpgsqlTransaction? transaction;

    public async Task<IReadOnlyList<QueuedFile>> EnqueueAsync(Guid userId, Guid batchId, IReadOnlyList<FileToImport> files, CancellationToken cancellationToken)
    {
        var results = new List<QueuedFile>(files.Count);
        foreach (var file in files)
        {
            var gzip = Compress(file.Content);
            results.Add(await EnqueueOneAsync(userId, batchId, file, gzip, cancellationToken));
        }

        return results;
    }

    private async Task<QueuedFile> EnqueueOneAsync(Guid userId, Guid batchId, FileToImport file, byte[] gzip, CancellationToken cancellationToken)
    {
        // A file that failed before is queued again (the parser may have been fixed since); any other
        // known file is reported as already imported.
        const string Sql = """
            INSERT INTO import.imported_files
                (id, user_id, batch_id, file_name, content_sha256, content_gzip, size_bytes, status, attempts, created_at)
            VALUES (@id, @user_id, @batch_id, @file_name, @sha, @gzip, @size, 'pending', 0, @now)
            ON CONFLICT (user_id, content_sha256) DO UPDATE
                SET status = 'pending', attempts = 0, locked_until = NULL, error_code = NULL, kind = NULL,
                    hands_imported = NULL, hands_already_present = NULL, hands_rejected = NULL, rejections = NULL,
                    completed_at = NULL, batch_id = EXCLUDED.batch_id, file_name = EXCLUDED.file_name
                WHERE imported_files.status = 'failed'
            RETURNING id
            """;

        // Two attempts: the existing row may be deleted (account removal) between the insert and the read.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using (var insert = await CommandAsync(Sql, cancellationToken))
            {
                Add(insert, "id", NpgsqlDbType.Uuid, Guid.CreateVersion7(time.GetUtcNow()));
                Add(insert, "user_id", NpgsqlDbType.Uuid, userId);
                Add(insert, "batch_id", NpgsqlDbType.Uuid, batchId);
                Add(insert, "file_name", NpgsqlDbType.Text, file.FileName);
                Add(insert, "sha", NpgsqlDbType.Char, file.ContentSha256);
                Add(insert, "gzip", NpgsqlDbType.Bytea, gzip);
                Add(insert, "size", NpgsqlDbType.Bigint, (long)file.Content.Length);
                Add(insert, "now", NpgsqlDbType.TimestampTz, time.GetUtcNow());
                if (await insert.ExecuteScalarAsync(cancellationToken) is Guid queuedId)
                {
                    return new QueuedFile(queuedId, file.FileName, ImportFileStatus.Pending, AlreadyImported: false);
                }
            }

            await using var select = await CommandAsync(
                "SELECT id, status FROM import.imported_files WHERE user_id = @user_id AND content_sha256 = @sha",
                cancellationToken);
            Add(select, "user_id", NpgsqlDbType.Uuid, userId);
            Add(select, "sha", NpgsqlDbType.Char, file.ContentSha256);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return new QueuedFile(reader.GetGuid(0), file.FileName, ImportStatusText.ToStatus(reader.GetString(1)), AlreadyImported: true);
            }
        }

        throw new InvalidOperationException("The imported file disappeared while being queued twice in a row.");
    }

    public async Task<IReadOnlyList<ImportedFileView>> GetBatchAsync(Guid userId, Guid batchId, CancellationToken cancellationToken)
    {
        var rows = await (
            from file in db.Set<ImportedFileRecord>().AsNoTracking()
            join account in db.Set<PokerAccountRecord>() on file.PokerAccountId equals (Guid?)account.Id into accounts
            from account in accounts.DefaultIfEmpty()
            where file.UserId == userId && file.BatchId == batchId
            orderby file.FileName
            select new
            {
                file.Id,
                file.FileName,
                file.Status,
                file.Kind,
                file.ErrorCode,
                ScreenName = account == null ? null : account.ScreenName,
                file.HandsImported,
                file.HandsAlreadyPresent,
                file.HandsRejected,
                file.CreatedAt,
                file.CompletedAt,
            }).ToListAsync(cancellationToken);

        return rows.Select(r => new ImportedFileView(
                r.Id,
                r.FileName,
                ImportStatusText.ToStatus(r.Status),
                ImportStatusText.ToKind(r.Kind),
                r.ErrorCode,
                r.ScreenName,
                r.HandsImported,
                r.HandsAlreadyPresent,
                r.HandsRejected,
                r.CreatedAt,
                r.CompletedAt))
            .ToList();
    }

    public async Task<ClaimedFile?> ClaimNextAsync(int maxAttempts, CancellationToken cancellationToken)
    {
        // Claims abandoned by crashed workers too many times: give up on those files.
        const string ExhaustSql = """
            UPDATE import.imported_files
            SET status = 'failed', error_code = @error_code, locked_until = NULL, completed_at = @now
            WHERE status = 'processing' AND locked_until < @now AND attempts >= @max_attempts
            """;

        // SKIP LOCKED: concurrent workers each take a different file instead of waiting on the same one.
        const string ClaimSql = """
            UPDATE import.imported_files AS f
            SET status = 'processing', attempts = f.attempts + 1, locked_until = @claim_until
            FROM (
                SELECT id FROM import.imported_files
                WHERE status IN ('pending', 'processing')
                  AND (locked_until IS NULL OR locked_until < @now)
                  AND attempts < @max_attempts
                ORDER BY created_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED
            ) AS next
            WHERE f.id = next.id
            RETURNING f.id, f.user_id, f.file_name, f.content_gzip, f.attempts
            """;

        var now = time.GetUtcNow();
        await using (var exhaust = await CommandAsync(ExhaustSql, cancellationToken))
        {
            Add(exhaust, "error_code", NpgsqlDbType.Text, ImportErrorCodes.ProcessingFailed);
            Add(exhaust, "now", NpgsqlDbType.TimestampTz, now);
            Add(exhaust, "max_attempts", NpgsqlDbType.Integer, maxAttempts);
            await exhaust.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var claim = await CommandAsync(ClaimSql, cancellationToken);
        Add(claim, "now", NpgsqlDbType.TimestampTz, now);
        Add(claim, "claim_until", NpgsqlDbType.TimestampTz, now + ClaimDuration);
        Add(claim, "max_attempts", NpgsqlDbType.Integer, maxAttempts);
        await using var reader = await claim.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ClaimedFile(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            Decompress(reader.GetFieldValue<byte[]>(3)),
            reader.GetInt32(4));
    }

    public async Task CompleteAsync(Guid fileId, FileOutcome outcome, CancellationToken cancellationToken)
    {
        const string Sql = """
            UPDATE import.imported_files
            SET status = @status, kind = @kind, error_code = @error_code, poker_account_id = @poker_account_id,
                hands_imported = @hands_imported, hands_already_present = @hands_already_present,
                hands_rejected = @hands_rejected, rejections = @rejections, locked_until = NULL, completed_at = @now
            WHERE id = @id
            """;

        await using var command = await CommandAsync(Sql, cancellationToken);
        Add(command, "id", NpgsqlDbType.Uuid, fileId);
        Add(command, "status", NpgsqlDbType.Text, ImportStatusText.From(outcome.Status));
        Add(command, "kind", NpgsqlDbType.Text, ImportStatusText.From(outcome.Kind));
        Add(command, "error_code", NpgsqlDbType.Text, outcome.ErrorCode);
        Add(command, "poker_account_id", NpgsqlDbType.Uuid, outcome.PokerAccountId);
        Add(command, "hands_imported", NpgsqlDbType.Integer, outcome.HandsImported);
        Add(command, "hands_already_present", NpgsqlDbType.Integer, outcome.HandsAlreadyPresent);
        Add(command, "hands_rejected", NpgsqlDbType.Integer, outcome.HandsRejected);
        Add(command, "rejections", NpgsqlDbType.Jsonb, outcome.Rejections.Count == 0 ? null : JsonSerializer.Serialize(outcome.Rejections, JsonOptions));
        Add(command, "now", NpgsqlDbType.TimestampTz, time.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ReleaseAfterErrorAsync(Guid fileId, int maxAttempts, CancellationToken cancellationToken)
    {
        const string Sql = """
            UPDATE import.imported_files
            SET status = CASE WHEN attempts >= @max_attempts THEN 'failed' ELSE 'pending' END,
                error_code = CASE WHEN attempts >= @max_attempts THEN @error_code END,
                completed_at = CASE WHEN attempts >= @max_attempts THEN @now END,
                locked_until = CASE WHEN attempts >= @max_attempts THEN NULL ELSE @retry_at END
            WHERE id = @id AND status = 'processing'
            """;

        var now = time.GetUtcNow();
        await using var command = await CommandAsync(Sql, cancellationToken);
        Add(command, "id", NpgsqlDbType.Uuid, fileId);
        Add(command, "max_attempts", NpgsqlDbType.Integer, maxAttempts);
        Add(command, "error_code", NpgsqlDbType.Text, ImportErrorCodes.ProcessingFailed);
        Add(command, "now", NpgsqlDbType.TimestampTz, now);
        Add(command, "retry_at", NpgsqlDbType.TimestampTz, now + RetryDelay);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            throw new InvalidOperationException("Nested import transactions are not supported.");
        }

        await using var efTransaction = await db.Database.BeginTransactionAsync(cancellationToken);
        transaction = (NpgsqlTransaction)efTransaction.GetDbTransaction();
        try
        {
            var result = await work(cancellationToken);
            await efTransaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            // Disposing without commit rolls back.
            transaction = null;
        }
    }

    public async Task<Guid> GetOrCreatePokerAccountAsync(Guid userId, PokerRoom room, string screenName, CancellationToken cancellationToken)
    {
        // Two statements on purpose: in one statement (CTE), a row inserted concurrently by another
        // transaction would be invisible to the read part, and the account would come back empty.
        await using (var insert = await CommandAsync(
            """
            INSERT INTO poker.poker_accounts (id, user_id, room, screen_name, created_at)
            VALUES (@id, @user_id, @room, @screen_name, @now)
            ON CONFLICT (user_id, room, screen_name) DO NOTHING
            """,
            cancellationToken))
        {
            Add(insert, "id", NpgsqlDbType.Uuid, Guid.CreateVersion7(time.GetUtcNow()));
            Add(insert, "user_id", NpgsqlDbType.Uuid, userId);
            Add(insert, "room", NpgsqlDbType.Integer, (int)room);
            Add(insert, "screen_name", NpgsqlDbType.Text, screenName);
            Add(insert, "now", NpgsqlDbType.TimestampTz, time.GetUtcNow());
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var select = await CommandAsync(
            "SELECT id FROM poker.poker_accounts WHERE user_id = @user_id AND room = @room AND screen_name = @screen_name",
            cancellationToken);
        Add(select, "user_id", NpgsqlDbType.Uuid, userId);
        Add(select, "room", NpgsqlDbType.Integer, (int)room);
        Add(select, "screen_name", NpgsqlDbType.Text, screenName);
        return (Guid)(await select.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Poker account not found right after its creation."));
    }

    public async Task<Guid> EnsureTournamentAsync(Guid pokerAccountId, string externalTournamentId, ParsedHand hand, CancellationToken cancellationToken)
    {
        // DO UPDATE (not DO NOTHING) so that RETURNING always yields the id, and to complete facts a
        // summary imported first could not provide. Values already known are never overwritten.
        const string Sql = """
            INSERT INTO poker.tournaments
                (id, poker_account_id, external_tournament_id, name, currency, buy_in_excluding_fee, fee, first_hand_at, created_at)
            VALUES (@id, @poker_account_id, @external_id, @name, @currency, @buy_in, @fee, @first_hand_at, @now)
            ON CONFLICT (poker_account_id, external_tournament_id) DO UPDATE
                SET currency = COALESCE(tournaments.currency, EXCLUDED.currency),
                    buy_in_excluding_fee = COALESCE(tournaments.buy_in_excluding_fee, EXCLUDED.buy_in_excluding_fee),
                    fee = COALESCE(tournaments.fee, EXCLUDED.fee),
                    first_hand_at = LEAST(tournaments.first_hand_at, EXCLUDED.first_hand_at)
            RETURNING id
            """;

        await using var command = await CommandAsync(Sql, cancellationToken);
        Add(command, "id", NpgsqlDbType.Uuid, Guid.CreateVersion7(time.GetUtcNow()));
        Add(command, "poker_account_id", NpgsqlDbType.Uuid, pokerAccountId);
        Add(command, "external_id", NpgsqlDbType.Text, externalTournamentId);
        Add(command, "name", NpgsqlDbType.Text, hand.TournamentName);
        Add(command, "currency", NpgsqlDbType.Text, hand.BuyIn.Currency);
        Add(command, "buy_in", NpgsqlDbType.Numeric, hand.BuyIn.AmountExcludingFee);
        Add(command, "fee", NpgsqlDbType.Numeric, hand.BuyIn.Fee);
        Add(command, "first_hand_at", NpgsqlDbType.TimestampTz, hand.StartedAt.ToUniversalTime());
        Add(command, "now", NpgsqlDbType.TimestampTz, time.GetUtcNow());
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task UpsertTournamentSummaryAsync(Guid pokerAccountId, ParsedTournamentSummary summary, CancellationToken cancellationToken)
    {
        // The summary is the authoritative record of the tournament: its values replace earlier ones.
        const string Sql = """
            INSERT INTO poker.tournaments
                (id, poker_account_id, external_tournament_id, name, currency, fee, prize_pool_buy_in, bounty_buy_in,
                 registered_players, mode, tournament_type, speed, flight_id, prize_pool, started_at, played_duration,
                 finish_position, prize_winnings, bounty_winnings, summary_imported_at, created_at)
            VALUES (@id, @poker_account_id, @external_id, @name, @currency, @fee, @prize_pool_buy_in, @bounty_buy_in,
                 @registered_players, @mode, @tournament_type, @speed, @flight_id, @prize_pool, @started_at, @played_duration,
                 @finish_position, @prize_winnings, @bounty_winnings, @now, @now)
            ON CONFLICT (poker_account_id, external_tournament_id) DO UPDATE
                SET name = EXCLUDED.name, currency = EXCLUDED.currency, fee = EXCLUDED.fee,
                    prize_pool_buy_in = EXCLUDED.prize_pool_buy_in, bounty_buy_in = EXCLUDED.bounty_buy_in,
                    registered_players = EXCLUDED.registered_players, mode = EXCLUDED.mode,
                    tournament_type = EXCLUDED.tournament_type, speed = EXCLUDED.speed, flight_id = EXCLUDED.flight_id,
                    prize_pool = EXCLUDED.prize_pool, started_at = EXCLUDED.started_at,
                    played_duration = EXCLUDED.played_duration, finish_position = EXCLUDED.finish_position,
                    prize_winnings = EXCLUDED.prize_winnings, bounty_winnings = EXCLUDED.bounty_winnings,
                    summary_imported_at = EXCLUDED.summary_imported_at
            """;

        await using var command = await CommandAsync(Sql, cancellationToken);
        Add(command, "id", NpgsqlDbType.Uuid, Guid.CreateVersion7(time.GetUtcNow()));
        Add(command, "poker_account_id", NpgsqlDbType.Uuid, pokerAccountId);
        Add(command, "external_id", NpgsqlDbType.Text, summary.ExternalTournamentId);
        Add(command, "name", NpgsqlDbType.Text, summary.TournamentName);
        Add(command, "currency", NpgsqlDbType.Text, summary.Currency);
        Add(command, "fee", NpgsqlDbType.Numeric, summary.Fee);
        Add(command, "prize_pool_buy_in", NpgsqlDbType.Numeric, summary.PrizePoolBuyIn);
        Add(command, "bounty_buy_in", NpgsqlDbType.Numeric, summary.BountyBuyIn);
        Add(command, "registered_players", NpgsqlDbType.Integer, summary.RegisteredPlayers);
        Add(command, "mode", NpgsqlDbType.Text, summary.Mode);
        Add(command, "tournament_type", NpgsqlDbType.Text, summary.Type);
        Add(command, "speed", NpgsqlDbType.Text, summary.Speed);
        Add(command, "flight_id", NpgsqlDbType.Text, summary.FlightId);
        Add(command, "prize_pool", NpgsqlDbType.Numeric, summary.PrizePool);
        Add(command, "started_at", NpgsqlDbType.TimestampTz, summary.StartedAt.ToUniversalTime());
        Add(command, "played_duration", NpgsqlDbType.Interval, summary.PlayedDuration);
        Add(command, "finish_position", NpgsqlDbType.Integer, summary.FinishPosition);
        Add(command, "prize_winnings", NpgsqlDbType.Numeric, summary.PrizeWinnings);
        Add(command, "bounty_winnings", NpgsqlDbType.Numeric, summary.BountyWinnings);
        Add(command, "now", NpgsqlDbType.TimestampTz, time.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TryInsertHandAsync(Guid pokerAccountId, Guid tournamentId, Guid importedFileId, ParsedHand hand, CancellationToken cancellationToken)
    {
        const string Sql = """
            INSERT INTO poker.hands
                (id, poker_account_id, tournament_id, imported_file_id, external_hand_id, started_at, level, small_blind,
                 big_blind, ante, table_name, max_seats, button_seat, hero_seat, hero_stack, hero_cards, total_pot, details, created_at)
            VALUES (@id, @poker_account_id, @tournament_id, @imported_file_id, @external_hand_id, @started_at, @level, @small_blind,
                 @big_blind, @ante, @table_name, @max_seats, @button_seat, @hero_seat, @hero_stack, @hero_cards, @total_pot, @details, @now)
            ON CONFLICT (poker_account_id, external_hand_id) DO NOTHING
            """;

        var heroSeat = hand.HeroName is null ? null : hand.Seats.FirstOrDefault(s => s.PlayerName == hand.HeroName);

        await using var command = await CommandAsync(Sql, cancellationToken);
        Add(command, "id", NpgsqlDbType.Uuid, Guid.CreateVersion7(time.GetUtcNow()));
        Add(command, "poker_account_id", NpgsqlDbType.Uuid, pokerAccountId);
        Add(command, "tournament_id", NpgsqlDbType.Uuid, tournamentId);
        Add(command, "imported_file_id", NpgsqlDbType.Uuid, importedFileId);
        Add(command, "external_hand_id", NpgsqlDbType.Text, hand.ExternalHandId);
        Add(command, "started_at", NpgsqlDbType.TimestampTz, hand.StartedAt.ToUniversalTime());
        Add(command, "level", NpgsqlDbType.Integer, hand.Level);
        Add(command, "small_blind", NpgsqlDbType.Bigint, hand.SmallBlind);
        Add(command, "big_blind", NpgsqlDbType.Bigint, hand.BigBlind);
        Add(command, "ante", NpgsqlDbType.Bigint, hand.Ante);
        Add(command, "table_name", NpgsqlDbType.Text, hand.TableName);
        Add(command, "max_seats", NpgsqlDbType.Integer, hand.MaxSeats);
        Add(command, "button_seat", NpgsqlDbType.Integer, hand.ButtonSeat);
        Add(command, "hero_seat", NpgsqlDbType.Integer, heroSeat?.SeatNumber);
        Add(command, "hero_stack", NpgsqlDbType.Bigint, heroSeat?.Stack);
        Add(command, "hero_cards", NpgsqlDbType.Text, hand.HeroCards.Count == 0 ? null : string.Concat(hand.HeroCards));
        Add(command, "total_pot", NpgsqlDbType.Bigint, hand.TotalPot);
        Add(command, "details", NpgsqlDbType.Jsonb, HandDetailsDocument.From(hand).ToJson());
        Add(command, "now", NpgsqlDbType.TimestampTz, time.GetUtcNow());
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private async Task<NpgsqlCommand> CommandAsync(string sql, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            // Closed by EF Core when the scope (and the DbContext) is disposed.
            await db.Database.OpenConnectionAsync(cancellationToken);
        }

        return new NpgsqlCommand(sql, connection, transaction);
    }

    private static void Add(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });

    internal static byte[] Compress(byte[] content)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(content);
        }

        return output.ToArray();
    }

    internal static byte[] Decompress(byte[] gzip)
    {
        using var input = new GZipStream(new MemoryStream(gzip), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }
}
