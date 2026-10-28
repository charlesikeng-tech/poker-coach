using System.Globalization;
using System.Text;
using PokerCoach.Application.Hands;
using PokerCoach.Application.Tournaments;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Application.Coaching;

/// <summary>A hand in the tournament as the review page needs it.</summary>
public sealed record ReviewHand(
    Guid HandId,
    int Index,
    int Level,
    PokerPosition? Position,
    string? HeroCards,
    decimal StackInBigBlinds,
    decimal NetBigBlinds,
    bool IsKeyMoment,
    bool SawFlop,
    HandCommentaryNote? Commentary);

/// <summary>The full tournament review: tournament facts, debrief summary and per-hand commentary.</summary>
public sealed record TournamentReview(
    TournamentDetail Detail,
    ReportView<DebriefReport>? Debrief,
    IReadOnlyList<ReviewHand> Hands);

/// <summary>
/// Orchestrates the tournament review: combines the tournament detail, the existing debrief and
/// stored hand commentaries. Hand commentary is generated as a batch through the coaching model.
/// </summary>
public sealed class TournamentReviewService(
    TournamentDetailService tournaments,
    TournamentDebriefService debriefs,
    ITournamentDetailStore tournamentStore,
    ICoachingReportStore reports,
    AiUsageGuard guard,
    IOpponentStatsReader opponentStats,
    TimeProvider time)
{
    internal const string Kind = "hand-commentary";

    public async Task<TournamentReview?> GetAsync(Guid userId, Guid tournamentId, string language, CancellationToken cancellationToken)
    {
        var detail = await tournaments.GetAsync(userId, tournamentId, cancellationToken);
        if (detail is null) return null;

        var debrief = await debriefs.GetAsync(userId, tournamentId, language, cancellationToken);
        var hands = await BuildHandListAsync(userId, detail, language, cancellationToken);
        return new TournamentReview(detail, debrief, hands);
    }

    /// <summary>Generates commentary for the key moments of the tournament. Returns the updated review.</summary>
    public async Task<ReportOutcome<TournamentReview>> GenerateKeyMomentCommentaryAsync(
        Guid userId, Guid tournamentId, string language, StakesLevel stakesLevel, CancellationToken cancellationToken)
    {
        var detail = await tournaments.GetAsync(userId, tournamentId, cancellationToken);
        if (detail is null) return ReportOutcome<TournamentReview>.Fail(CoachingFailure.NotFound);
        if (detail.PendingHands > 0) return ReportOutcome<TournamentReview>.Fail(CoachingFailure.HandsPending);
        if (detail.Stack.Count < TournamentDebriefService.MinHands) return ReportOutcome<TournamentReview>.Fail(CoachingFailure.NotEnoughData);

        var keyMomentHandIds = detail.KeyMoments.Select(m => m.HandId).ToList();
        if (keyMomentHandIds.Count == 0) return ReportOutcome<TournamentReview>.Fail(CoachingFailure.NotEnoughData);

        var existing = await LoadCommentariesAsync(userId, keyMomentHandIds, language, cancellationToken);
        var missing = keyMomentHandIds.Where(id => !existing.ContainsKey(id)).ToList();

        if (missing.Count > 0)
        {
            var failure = await GenerateCommentaryBatchAsync(userId, detail, missing, language, cancellationToken);
            if (failure is not null) return ReportOutcome<TournamentReview>.Fail(failure.Value);
        }

        var debrief = await debriefs.GetAsync(userId, tournamentId, language, cancellationToken);
        var hands = await BuildHandListAsync(userId, detail, language, cancellationToken);
        return ReportOutcome<TournamentReview>.Of(new ReportView<TournamentReview>(
            new TournamentReview(detail, debrief, hands), false, string.Empty, time.GetUtcNow()));
    }

    /// <summary>Generates commentary for one hand of the tournament.</summary>
    public async Task<ReportOutcome<HandCommentaryNote>> GenerateSingleCommentaryAsync(
        Guid userId, Guid tournamentId, Guid handId, string language, CancellationToken cancellationToken)
    {
        var detail = await tournaments.GetAsync(userId, tournamentId, cancellationToken);
        if (detail is null) return ReportOutcome<HandCommentaryNote>.Fail(CoachingFailure.NotFound);

        var failure = await GenerateCommentaryBatchAsync(userId, detail, [handId], language, cancellationToken);
        if (failure is not null) return ReportOutcome<HandCommentaryNote>.Fail(failure.Value);

        var stored = await reports.FindAsync<HandCommentaryNote>(userId, Kind, handId.ToString("D"), language, cancellationToken);
        return stored is not null
            ? ReportOutcome<HandCommentaryNote>.Of(new ReportView<HandCommentaryNote>(stored.Report, false, stored.Model, stored.CreatedAt))
            : ReportOutcome<HandCommentaryNote>.Fail(CoachingFailure.ModelFailed);
    }

    private async Task<CoachingFailure?> GenerateCommentaryBatchAsync(
        Guid userId, TournamentDetail detail, IReadOnlyList<Guid> handIds, string language, CancellationToken cancellationToken)
    {
        var exampleHands = await reports.LoadHandsAsync(userId, handIds, cancellationToken);
        if (exampleHands.Count == 0) return CoachingFailure.NotFound;

        var opponentNames = HandNarrator.OpponentNames(exampleHands);
        var opponentTypes = opponentNames.Count > 0
            ? HandNarrator.ClassifyOpponents(await opponentStats.GetByUserAsync(userId, opponentNames, cancellationToken))
            : new Dictionary<string, PlayerType>();

        var stories = exampleHands
            .Select((h, i) => (Ref: $"H{i + 1}", h.HandId, Story: HandNarrator.Tell(h, opponentTypes)))
            .ToList();

        var prompt = new HandCommentaryPrompt(
            language,
            TournamentContext(detail),
            stories.Select(s => (s.Ref, s.Story)).ToList());

        var outcome = await guard.RunAsync(
            userId, AiTask.HandCommentary,
            async (model, ct) => await model.CommentHandsAsync(prompt, ct),
            cancellationToken);

        if (outcome.Answer is not { } answer) return outcome.Failure!.Value;

        var refToHandId = stories.ToDictionary(s => s.Ref, s => s.HandId, StringComparer.Ordinal);
        foreach (var note in answer.Value.Hands)
        {
            if (!refToHandId.TryGetValue(note.Ref, out var hid)) continue;
            var stored = new StoredReport<HandCommentaryNote>(note, string.Empty, answer.Usage.Model, time.GetUtcNow());
            await reports.SaveAsync(userId, Kind, hid.ToString("D"), language, stored, cancellationToken);
        }

        return null;
    }

    private async Task<IReadOnlyList<ReviewHand>> BuildHandListAsync(
        Guid userId, TournamentDetail detail, string language, CancellationToken cancellationToken)
    {
        var rows = await tournamentStore.GetHandsAsync(detail.Tournament.Id, HeroHandFacts.Version, cancellationToken);
        var keyMomentIds = detail.KeyMoments.Select(m => m.HandId).ToHashSet();
        var handIds = rows.Select(r => r.HandId).ToList();
        var commentaries = await LoadCommentariesAsync(userId, handIds, language, cancellationToken);

        return rows
            .Where(r => r.HeroStack is > 0)
            .Select((r, i) => new ReviewHand(
                r.HandId,
                i + 1,
                r.Level,
                r.Facts?.Position,
                r.HeroCards,
                r.Facts?.StackInBigBlinds ?? 0,
                r.Facts?.NetBigBlinds ?? 0,
                keyMomentIds.Contains(r.HandId),
                r.Facts?.SawFlop ?? false,
                commentaries.GetValueOrDefault(r.HandId)))
            .ToList();
    }

    private async Task<Dictionary<Guid, HandCommentaryNote>> LoadCommentariesAsync(
        Guid userId, IReadOnlyList<Guid> handIds, string language, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, HandCommentaryNote>();
        foreach (var id in handIds)
        {
            var stored = await reports.FindAsync<HandCommentaryNote>(userId, Kind, id.ToString("D"), language, cancellationToken);
            if (stored is not null) result[id] = stored.Report;
        }
        return result;
    }

    private static string TournamentContext(TournamentDetail detail)
    {
        var t = detail.Tournament;
        var text = new StringBuilder();
        string Money(decimal? amount) => amount is { } a
            ? $"{a.ToString("0.00", CultureInfo.InvariantCulture)} {t.Currency ?? string.Empty}".Trim()
            : "unknown";

        text.Append(CultureInfo.InvariantCulture, $"Tournament: buy-in {Money(t.BuyIn)}, ");
        text.Append(CultureInfo.InvariantCulture, $"field {(t.RegisteredPlayers is { } field ? field.ToString(CultureInfo.InvariantCulture) : "unknown")} players, ");
        text.Append(CultureInfo.InvariantCulture, $"type {detail.Type ?? "unknown"}, speed {detail.Speed ?? "unknown"}. ");
        text.Append(CultureInfo.InvariantCulture, $"{detail.Stack.Count} hands played.");
        return text.ToString();
    }
}
