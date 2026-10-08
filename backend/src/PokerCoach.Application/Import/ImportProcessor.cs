using System.Text;
using PokerCoach.HandHistories;

namespace PokerCoach.Application.Import;

/// <summary>
/// Processing side of the import: parses one queued file and stores what it contains, all in one
/// transaction (a crash leaves nothing half-imported; the file is simply processed again).
/// Re-processing is harmless: accounts, tournaments and hands are insert-or-get on their natural keys.
/// </summary>
public sealed class ImportProcessor(IImportStore store, IEnumerable<IHandHistoryProvider> providers, ImportOptions options)
{
    /// <summary>Enough to diagnose a file; the full count is kept separately.</summary>
    public const int MaxStoredRejections = 50;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly IReadOnlyList<IHandHistoryProvider> providers = providers.ToList();

    /// <returns>False when no file was waiting.</returns>
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var file = await store.ClaimNextAsync(options.MaxProcessingAttempts, cancellationToken);
        if (file is null)
        {
            return false;
        }

        try
        {
            await store.InTransactionAsync(
                async ct =>
                {
                    var outcome = await ImportAsync(file, ct);
                    await store.CompleteAsync(file.Id, outcome, ct);
                    return outcome;
                },
                cancellationToken);
        }
        catch
        {
            // Not cancellable: the claim must be given back even when the host is stopping.
            await store.ReleaseAfterErrorAsync(file.Id, options.MaxProcessingAttempts, CancellationToken.None);
            throw;
        }

        return true;
    }

    private async Task<FileOutcome> ImportAsync(ClaimedFile file, CancellationToken cancellationToken)
    {
        if (!TryDecode(file.Content, out var text))
        {
            return Failed(ImportErrorCodes.InvalidEncoding);
        }

        foreach (var provider in providers)
        {
            switch (provider.Detect(text))
            {
                case HandHistoryFileKind.HandHistory:
                    return await ImportHandsAsync(file, provider, text, cancellationToken);
                case HandHistoryFileKind.TournamentSummary:
                    return await ImportSummaryAsync(file, provider, text, cancellationToken);
                case HandHistoryFileKind.Unknown:
                default:
                    continue;
            }
        }

        return Failed(ImportErrorCodes.UnrecognizedFormat);
    }

    private async Task<FileOutcome> ImportSummaryAsync(ClaimedFile file, IHandHistoryProvider provider, string text, CancellationToken cancellationToken)
    {
        var result = provider.TournamentSummaries.Parse(text);
        if (result.Summary is not { } summary)
        {
            return Failed(ImportErrorCodes.InvalidSummary, ImportFileKind.TournamentSummary, result.Errors);
        }

        var accountId = await store.GetOrCreatePokerAccountAsync(file.UserId, provider.Room, summary.PlayerName, cancellationToken);
        await store.UpsertTournamentSummaryAsync(accountId, summary, cancellationToken);
        return new FileOutcome(ImportFileStatus.Completed, ImportFileKind.TournamentSummary, null, accountId, 0, 0, 0, []);
    }

    private async Task<FileOutcome> ImportHandsAsync(ClaimedFile file, IHandHistoryProvider provider, string text, CancellationToken cancellationToken)
    {
        var result = provider.HandHistories.Parse(text);
        var rejections = new List<ParseError>(result.Errors);
        var accounts = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var tournaments = new Dictionary<(Guid, string), Guid>();
        Guid? firstAccountId = null;
        int imported = 0, alreadyPresent = 0;

        foreach (var hand in result.Hands)
        {
            // Without the hero we cannot tell whose hand it is; without the tournament id we cannot group
            // it. Both are rejected rather than guessed.
            if (hand.HeroName is null)
            {
                rejections.Add(new ParseError(ImportErrorCodes.HeroNotFound, hand.LineNumber, hand.ExternalHandId));
                continue;
            }

            if (hand.ExternalTournamentId is null)
            {
                rejections.Add(new ParseError(ImportErrorCodes.TournamentUnknown, hand.LineNumber, hand.ExternalHandId));
                continue;
            }

            if (!accounts.TryGetValue(hand.HeroName, out var accountId))
            {
                accountId = await store.GetOrCreatePokerAccountAsync(file.UserId, provider.Room, hand.HeroName, cancellationToken);
                accounts[hand.HeroName] = accountId;
            }

            firstAccountId ??= accountId;

            if (!tournaments.TryGetValue((accountId, hand.ExternalTournamentId), out var tournamentId))
            {
                tournamentId = await store.EnsureTournamentAsync(accountId, hand.ExternalTournamentId, hand, cancellationToken);
                tournaments[(accountId, hand.ExternalTournamentId)] = tournamentId;
            }

            if (await store.TryInsertHandAsync(accountId, tournamentId, file.Id, hand, cancellationToken))
            {
                imported++;
            }
            else
            {
                alreadyPresent++;
            }
        }

        if (imported + alreadyPresent == 0)
        {
            return Failed(ImportErrorCodes.NoValidHand, ImportFileKind.HandHistory, rejections);
        }

        return new FileOutcome(
            ImportFileStatus.Completed,
            ImportFileKind.HandHistory,
            null,
            firstAccountId,
            imported,
            alreadyPresent,
            rejections.Count,
            Cap(rejections));
    }

    private static FileOutcome Failed(string code, ImportFileKind? kind = null, IReadOnlyList<ParseError>? rejections = null) =>
        new(ImportFileStatus.Failed, kind, code, null, 0, 0, rejections?.Count ?? 0, Cap(rejections ?? []));

    private static IReadOnlyList<ParseError> Cap(IReadOnlyList<ParseError> rejections) =>
        rejections.Count <= MaxStoredRejections ? rejections : rejections.Take(MaxStoredRejections).ToList();

    /// <summary>Strict UTF-8: a file in another encoding is refused rather than imported with mangled pseudonyms.</summary>
    internal static bool TryDecode(byte[] content, out string text)
    {
        try
        {
            text = StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }

        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        return true;
    }
}
