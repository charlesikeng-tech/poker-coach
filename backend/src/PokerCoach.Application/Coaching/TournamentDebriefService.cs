using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PokerCoach.Application.Leaks;
using PokerCoach.Application.Statistics;
using PokerCoach.Application.Tournaments;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Application.Coaching;

/// <summary>How the coach reads a key moment. Only <see cref="Variance"/> is checked against our figures.</summary>
public enum MomentVerdict
{
    WellPlayed,
    Mistake,

    /// <summary>The cards decided against the odds: kept only when a computed all-in equity says so.</summary>
    Variance,

    /// <summary>A normal play either way, or not enough information to judge.</summary>
    Standard,
}

public sealed record MomentNote(string Ref, MomentVerdict Verdict, string Note);

/// <summary>The model's debrief of one tournament, after validation (ADR-0012).</summary>
public sealed record TournamentDebrief(
    string Headline,
    string Story,
    IReadOnlyList<MomentNote> Moments,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> WorkOn);

/// <summary>What the model is asked: our facts and the key hands' anonymous stories. No name, no id.</summary>
public sealed record DebriefPrompt(string Language, string Facts, IReadOnlyList<(string Ref, string Story)> Moments);

/// <summary>A key moment as Poker Coach measured it, shown next to the coach's note: our facts, not the model's.</summary>
public sealed record DebriefMoment(
    string Ref,
    Guid HandId,
    int Level,
    PokerPosition? Position,
    string? HeroCards,
    decimal StackInBigBlinds,
    decimal NetBigBlinds,
    decimal StackShare,
    KeyMomentStage Stage,
    decimal? AllInEquity);

public sealed record DebriefReport(TournamentDebrief Debrief, IReadOnlyList<DebriefMoment> Moments);

/// <summary>
/// The coach's debrief of one tournament (ADR-0012): the story of the run, a read of each key moment, what
/// went well and what to work on. Everything measured comes from the tournament detail and the leak
/// analysis; the model writes, and its verdicts that contradict our figures are corrected.
/// </summary>
public sealed class TournamentDebriefService(
    TournamentDetailService tournaments,
    LeakService leaks,
    ICoachingReportStore reports,
    ICoachingStore ledger,
    ICoachingModel model,
    CoachingOptions options,
    TimeProvider time)
{
    internal const string Kind = "tournament-debrief";

    /// <summary>Below this many hands dealt, there is no tournament story to tell.</summary>
    public const int MinHands = 20;

    /// <summary>Bump when the instructions change in a way the player would see: stored debriefs show as out of date.</summary>
    internal const int Version = 1;

    /// <summary>Leaks are read over the same recent window as the weekly plan.</summary>
    private const int LeakDays = 90;

    private const int MinOpportunitiesShown = 5;

    private readonly CoachingGate gate = new(ledger, model, options, time);

    /// <summary>The stored debrief, if any, and whether the tournament's facts changed since. Free.</summary>
    public async Task<ReportView<DebriefReport>?> GetAsync(Guid userId, Guid tournamentId, string language, CancellationToken cancellationToken)
    {
        var stored = await reports.FindAsync<DebriefReport>(userId, Kind, Subject(tournamentId), language, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        var detail = await tournaments.GetAsync(userId, tournamentId, cancellationToken);
        return detail is null ? null : View(stored, Fingerprint(detail, language));
    }

    /// <summary>Writes the debrief, unless the stored one is still up to date (then it is returned, unpaid).</summary>
    public async Task<ReportOutcome<DebriefReport>> GenerateAsync(Guid userId, Guid tournamentId, string language, CancellationToken cancellationToken)
    {
        var detail = await tournaments.GetAsync(userId, tournamentId, cancellationToken);
        if (detail is null)
        {
            return ReportOutcome<DebriefReport>.Fail(CoachingFailure.NotFound);
        }

        if (detail.PendingHands > 0)
        {
            return ReportOutcome<DebriefReport>.Fail(CoachingFailure.HandsPending);
        }

        if (detail.Stack.Count < MinHands)
        {
            return ReportOutcome<DebriefReport>.Fail(CoachingFailure.NotEnoughData);
        }

        var fingerprint = Fingerprint(detail, language);
        var stored = await reports.FindAsync<DebriefReport>(userId, Kind, Subject(tournamentId), language, cancellationToken);
        if (stored is not null && stored.Fingerprint == fingerprint)
        {
            return ReportOutcome<DebriefReport>.Of(View(stored, fingerprint));
        }

        if (await gate.CheckAsync(userId, Kind, options.DailyDebriefsPerUser, cancellationToken) is { } refused)
        {
            return ReportOutcome<DebriefReport>.Fail(refused);
        }

        var analysis = await leaks.GetAsync(userId, null, time.GetUtcNow().AddDays(-LeakDays), null, cancellationToken);
        var moments = detail.KeyMoments
            .Select((m, i) => new DebriefMoment(
                $"M{i + 1}",
                m.HandId,
                m.Level,
                m.Position,
                m.HeroCards,
                m.StackInBigBlinds,
                m.Moment.NetBigBlinds,
                m.Moment.StackShare,
                m.Moment.Stage,
                m.AllInEquity))
            .ToList();
        var hands = (await reports.LoadHandsAsync(userId, moments.Select(m => m.HandId).ToList(), cancellationToken))
            .ToDictionary(h => h.HandId);
        var prompt = new DebriefPrompt(
            language,
            Facts(detail, moments, analysis.Report.Leaks),
            moments.Where(m => hands.ContainsKey(m.HandId)).Select(m => (m.Ref, HandNarrator.Tell(hands[m.HandId]))).ToList());

        ModelAnswer<TournamentDebrief> answer;
        try
        {
            answer = await model.DebriefTournamentAsync(prompt, cancellationToken);
        }
        catch (CoachingModelException)
        {
            return ReportOutcome<DebriefReport>.Fail(CoachingFailure.ModelFailed);
        }

        await gate.RecordAsync(userId, Kind, answer.Usage, cancellationToken);

        var report = new StoredReport<DebriefReport>(
            new DebriefReport(Checked(answer.Value, moments), moments),
            fingerprint,
            answer.Usage.Model,
            time.GetUtcNow());
        await reports.SaveAsync(userId, Kind, Subject(tournamentId), language, report, cancellationToken);
        return ReportOutcome<DebriefReport>.Of(View(report, fingerprint));
    }

    /// <summary>
    /// The model may only talk about moments we gave it, once each, and may only blame the cards when our
    /// computed equity shows the favourite lost: otherwise "variance" becomes "standard".
    /// </summary>
    internal static TournamentDebrief Checked(TournamentDebrief debrief, IReadOnlyList<DebriefMoment> moments)
    {
        var byRef = moments.ToDictionary(m => m.Ref, StringComparer.Ordinal);
        var order = moments.Select((m, i) => (m.Ref, i)).ToDictionary(x => x.Ref, x => x.i, StringComparer.Ordinal);
        var notes = debrief.Moments
            .Where(n => byRef.ContainsKey(n.Ref))
            .DistinctBy(n => n.Ref)
            .Select(n => n.Verdict == MomentVerdict.Variance && !AgainstTheOdds(byRef[n.Ref]) ? n with { Verdict = MomentVerdict.Standard } : n)
            .OrderBy(n => order[n.Ref])
            .ToList();
        return debrief with { Moments = notes };
    }

    private static bool AgainstTheOdds(DebriefMoment moment) =>
        moment.AllInEquity is { } equity
        && ((equity > 0.5m && moment.NetBigBlinds < 0) || (equity < 0.5m && moment.NetBigBlinds > 0));

    /// <summary>The tournament in plain facts, all computed by us. No name, no date, no id.</summary>
    internal static string Facts(TournamentDetail detail, IReadOnlyList<DebriefMoment> moments, IReadOnlyList<Leak> leaks)
    {
        var t = detail.Tournament;
        var r = t.Result;
        var text = new StringBuilder();
        string Money(decimal? amount) => amount is { } a ? $"{a.ToString("0.00", CultureInfo.InvariantCulture)} {t.Currency ?? string.Empty}".Trim() : "unknown";
        string Pct(decimal ratio) => (ratio * 100m).ToString("0.#", CultureInfo.InvariantCulture) + " %";
        string Bb(decimal value) => value.ToString("0.#", CultureInfo.InvariantCulture) + " BB";

        text.AppendLine("Tournament:");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Buy-in {Money(t.BuyIn)}; field {(t.RegisteredPlayers is { } field ? field.ToString(CultureInfo.InvariantCulture) : "unknown")} players; type {detail.Type ?? "unknown"}, speed {detail.Speed ?? "unknown"}.");
        if (r.Status == TournamentResultStatus.Known)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- Entries: {r.Entries}. Finish: {(t.FinishPosition is { } place ? place.ToString(CultureInfo.InvariantCulture) : "unknown")}. Prize {Money(r.PrizeWinnings)}, bounties {Money(r.BountyWinnings)}, profit {Money(r.Profit)}.");
        }
        else
        {
            text.AppendLine("- Result unknown: the tournament summary was not imported. Do not guess the finish or the money.");
        }

        if (detail.HandsDuration is { } duration)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- Played {detail.Stack.Count} hands over {(int)duration.TotalMinutes} minutes, levels {detail.Stack[0].Level} to {detail.Stack[^1].Level}.");
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"- Stack: started at {Bb(detail.Stack[0].StackInBigBlinds)}, peaked at {Bb(detail.Stack.Max(p => p.StackInBigBlinds))}, last hand at {Bb(detail.Stack[^1].StackInBigBlinds)}.");

        text.AppendLine();
        text.AppendLine("This tournament's statistics (one tournament is a small sample: describe, never call them leaks):");
        foreach (var (name, rate) in Rates(detail.Stats))
        {
            if (rate.Opportunities >= MinOpportunitiesShown && rate.Rate is { } value)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {name}: {Pct(value)} ({rate.Made}/{rate.Opportunities}).");
            }
        }

        var confirmed = leaks.Where(l => l.Confidence == LeakConfidence.Confirmed).Take(5).ToList();
        text.AppendLine();
        text.AppendLine(confirmed.Count == 0
            ? "The player's long-term leaks (last 90 days): none confirmed."
            : "The player's long-term leaks (last 90 days, measured over many hands). Link a moment to one only if the hand clearly shows it:");
        foreach (var leak in confirmed)
        {
            var where = leak.Position is { } p ? $" from {HandNarrator.Label(p)}" : string.Empty;
            text.AppendLine(CultureInfo.InvariantCulture, $"- {leak.Stat}{where} {(leak.Direction == LeakDirection.TooHigh ? "too high" : "too low")}: {Pct(leak.Rate)} against a reference of {Pct(leak.Range.Min)}–{Pct(leak.Range.Max)}.");
        }

        text.AppendLine();
        text.AppendLine(moments.Count == 0
            ? "Key moments: no single hand moved a quarter of the stack or more."
            : "Key moments (hands that won or lost at least a quarter of the stack), in play order:");
        foreach (var m in moments)
        {
            text.Append(CultureInfo.InvariantCulture, $"- {m.Ref}: level {m.Level}, stack {Bb(m.StackInBigBlinds)}, result {(m.NetBigBlinds >= 0 ? "+" : string.Empty)}{Bb(m.NetBigBlinds)} ({(m.StackShare >= 0 ? "+" : string.Empty)}{Pct(m.StackShare)} of the stack), decided {Stage(m.Stage)}");
            text.AppendLine(m.AllInEquity is { } equity ? $"; preflop all-in, hero's computed equity {Pct(equity)}." : ".");
        }

        return text.ToString();
    }

    internal static string Fingerprint(TournamentDetail detail, string language)
    {
        var r = detail.Tournament.Result;
        var key = string.Join(
            '|',
            Version.ToString(CultureInfo.InvariantCulture),
            detail.Tournament.Id,
            detail.Stack.Count.ToString(CultureInfo.InvariantCulture),
            detail.PendingHands.ToString(CultureInfo.InvariantCulture),
            r.Status,
            r.Profit?.ToString(CultureInfo.InvariantCulture) ?? "-",
            detail.Tournament.FinishPosition?.ToString(CultureInfo.InvariantCulture) ?? "-",
            HeroHandFacts.Version.ToString(CultureInfo.InvariantCulture),
            language);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    private static ReportView<DebriefReport> View(StoredReport<DebriefReport> stored, string fingerprint) =>
        new(stored.Report, stored.Fingerprint != fingerprint, stored.Model, stored.CreatedAt);

    private static string Subject(Guid tournamentId) => tournamentId.ToString("D");

    private static string Stage(KeyMomentStage stage) => stage switch
    {
        KeyMomentStage.Preflop => "preflop",
        KeyMomentStage.Postflop => "after the flop, before showdown",
        _ => "at showdown",
    };

    private static IEnumerable<(string Name, StatRate Rate)> Rates(StatLine s) =>
    [
        ("VPIP", s.Vpip),
        ("PFR", s.Pfr),
        ("Raise first in", s.Rfi),
        ("Steal", s.Steal),
        ("3-bet", s.ThreeBet),
        ("Fold to 3-bet", s.FoldToThreeBet),
        ("C-bet flop", s.CbetFlop),
        ("Went to showdown", s.WentToShowdown),
        ("Won at showdown", s.WonAtShowdown),
    ];
}
