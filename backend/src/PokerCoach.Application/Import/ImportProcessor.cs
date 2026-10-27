using System.Text;
using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
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
        var handsToInsert = new List<HandToInsert>(result.Hands.Count);
        Guid? firstAccountId = null;

        foreach (var hand in result.Hands)
        {
            if (hand.HeroName is null)
            {
                rejections.Add(new ParseError(ImportErrorCodes.HeroNotFound, hand.LineNumber, hand.ExternalHandId));
                continue;
            }

            if (hand.GameType == GameType.Tournament && hand.ExternalTournamentId is null)
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

            Guid? tournamentId = null;
            if (hand.ExternalTournamentId is not null)
            {
                if (!tournaments.TryGetValue((accountId, hand.ExternalTournamentId), out var tid))
                {
                    tid = await store.EnsureTournamentAsync(accountId, hand.ExternalTournamentId, hand, cancellationToken);
                    tournaments[(accountId, hand.ExternalTournamentId)] = tid;
                }

                tournamentId = tid;
            }

            handsToInsert.Add(new HandToInsert(accountId, tournamentId, file.Id, hand));
        }

        if (handsToInsert.Count == 0)
        {
            return Failed(ImportErrorCodes.NoValidHand, ImportFileKind.HandHistory, rejections);
        }

        var imported = await store.BulkInsertHandsAsync(handsToInsert, cancellationToken);
        var alreadyPresent = handsToInsert.Count - imported;

        if (imported > 0)
        {
            var opponentStats = AggregateOpponentFacts(handsToInsert);
            if (opponentStats.Count > 0)
                await store.UpsertOpponentStatsAsync(opponentStats, cancellationToken);
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

    private static List<OpponentStatsToUpsert> AggregateOpponentFacts(IReadOnlyList<HandToInsert> hands)
    {
        var agg = new Dictionary<(Guid, string), (int Hands, int Vpip, int Pfr, int ThreeBetOpp, int ThreeBet, int SawFlop, int Wtsd, int Aggressive, int Decisions)>();

        foreach (var item in hands)
        {
            var analysis = HandForAnalysisMapper.From(item.Hand);
            if (analysis is null) continue;

            var facts = OpponentFactsExtractor.Extract(analysis);
            foreach (var (opponent, f) in facts)
            {
                var key = (item.PokerAccountId, opponent);
                var prev = agg.GetValueOrDefault(key);
                agg[key] = (
                    prev.Hands + 1,
                    prev.Vpip + (f.Vpip ? 1 : 0),
                    prev.Pfr + (f.Pfr ? 1 : 0),
                    prev.ThreeBetOpp + (f.ThreeBetOpportunity ? 1 : 0),
                    prev.ThreeBet + (f.ThreeBet ? 1 : 0),
                    prev.SawFlop + (f.SawFlop ? 1 : 0),
                    prev.Wtsd + (f.WentToShowdown ? 1 : 0),
                    prev.Aggressive + f.PostflopAggressive,
                    prev.Decisions + f.PostflopDecisions);
            }
        }

        return agg.Select(kv => new OpponentStatsToUpsert(
            kv.Key.Item1,
            kv.Key.Item2,
            kv.Value.Hands,
            kv.Value.Vpip,
            kv.Value.Pfr,
            kv.Value.ThreeBetOpp,
            kv.Value.ThreeBet,
            kv.Value.SawFlop,
            kv.Value.Wtsd,
            kv.Value.Aggressive,
            kv.Value.Decisions)).ToList();
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
