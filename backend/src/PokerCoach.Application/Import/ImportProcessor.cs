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

        var insertedIds = await store.BulkInsertHandsAsync(handsToInsert, cancellationToken);
        var alreadyPresent = handsToInsert.Count - insertedIds.Count;

        if (insertedIds.Count > 0)
        {
            var newHands = handsToInsert.Where(h => insertedIds.Contains(h.Hand.ExternalHandId)).ToList();
            var opponentStats = AggregateOpponentFacts(newHands);
            if (opponentStats.Count > 0)
                await store.UpsertOpponentStatsAsync(opponentStats, cancellationToken);
        }

        return new FileOutcome(
            ImportFileStatus.Completed,
            ImportFileKind.HandHistory,
            null,
            firstAccountId,
            insertedIds.Count,
            alreadyPresent,
            rejections.Count,
            Cap(rejections));
    }

    private static List<OpponentStatsToUpsert> AggregateOpponentFacts(IReadOnlyList<HandToInsert> hands)
    {
        var agg = new Dictionary<(Guid, string), OpponentAgg>();

        foreach (var item in hands)
        {
            var analysis = HandForAnalysisMapper.From(item.Hand);
            if (analysis is null) continue;

            var facts = OpponentFactsExtractor.Extract(analysis);
            foreach (var (opponent, f) in facts)
            {
                var key = (item.PokerAccountId, opponent);
                if (!agg.TryGetValue(key, out var a))
                {
                    a = new OpponentAgg();
                    agg[key] = a;
                }

                a.Hands++;
                a.Vpip += f.Vpip ? 1 : 0;
                a.Pfr += f.Pfr ? 1 : 0;
                a.RfiOpp += f.RfiOpportunity ? 1 : 0;
                a.Rfi += f.Rfi ? 1 : 0;
                a.StealOpp += f.StealOpportunity ? 1 : 0;
                a.Steal += f.Steal ? 1 : 0;
                a.ThreeBetOpp += f.ThreeBetOpportunity ? 1 : 0;
                a.ThreeBet += f.ThreeBet ? 1 : 0;
                a.FoldToThreeBetOpp += f.FoldToThreeBetOpportunity ? 1 : 0;
                a.FoldToThreeBet += f.FoldToThreeBet ? 1 : 0;
                a.SawFlop += f.SawFlop ? 1 : 0;
                a.CbetFlopOpp += f.CbetFlopOpportunity ? 1 : 0;
                a.CbetFlop += f.CbetFlop ? 1 : 0;
                a.FoldToCbetOpp += f.FoldToCbetFlopOpportunity ? 1 : 0;
                a.FoldToCbet += f.FoldToCbetFlop ? 1 : 0;
                a.Wtsd += f.WentToShowdown ? 1 : 0;
                a.Wsd += f.WonAtShowdown ? 1 : 0;
                a.Aggressive += f.PostflopAggressive;
                a.Decisions += f.PostflopDecisions;

                var startedAt = item.Hand.StartedAt;
                if (a.FirstSeen == DateTimeOffset.MinValue || startedAt < a.FirstSeen)
                    a.FirstSeen = startedAt;
                if (startedAt > a.LastSeen)
                    a.LastSeen = startedAt;
                if (item.Hand.ExternalTournamentId is not null)
                    a.Tournaments.Add(item.Hand.ExternalTournamentId);
            }
        }

        return agg.Select(kv => new OpponentStatsToUpsert(
            kv.Key.Item1, kv.Key.Item2,
            kv.Value.Hands, kv.Value.Vpip, kv.Value.Pfr,
            kv.Value.RfiOpp, kv.Value.Rfi,
            kv.Value.StealOpp, kv.Value.Steal,
            kv.Value.ThreeBetOpp, kv.Value.ThreeBet,
            kv.Value.FoldToThreeBetOpp, kv.Value.FoldToThreeBet,
            kv.Value.SawFlop,
            kv.Value.CbetFlopOpp, kv.Value.CbetFlop,
            kv.Value.FoldToCbetOpp, kv.Value.FoldToCbet,
            kv.Value.Wtsd, kv.Value.Wsd,
            kv.Value.Aggressive, kv.Value.Decisions,
            kv.Value.FirstSeen, null)).ToList();
    }

    private sealed class OpponentAgg
    {
        public int Hands, Vpip, Pfr;
        public int RfiOpp, Rfi, StealOpp, Steal;
        public int ThreeBetOpp, ThreeBet, FoldToThreeBetOpp, FoldToThreeBet;
        public int SawFlop, CbetFlopOpp, CbetFlop, FoldToCbetOpp, FoldToCbet;
        public int Wtsd, Wsd, Aggressive, Decisions;
        public DateTimeOffset FirstSeen, LastSeen;
        public HashSet<string> Tournaments = new(StringComparer.Ordinal);
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
