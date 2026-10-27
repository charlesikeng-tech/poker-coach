using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PokerCoach.Application.Leaks;
using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;

namespace PokerCoach.Application.Coaching;

public sealed class PlayerProfileService(
    StatisticsService statistics,
    ILeakService leaks,
    ICoachingReportStore reports,
    AiUsageGuard guard,
    TimeProvider time)
{
    internal const string Kind = "player-profile";
    public const int MinHands = 200;
    internal const int Version = 1;

    public async Task<ReportView<PlayerProfile>?> GetAsync(Guid userId, GameType gameType, string language, CancellationToken cancellationToken)
    {
        var subject = Subject(gameType);
        var stored = await reports.FindAsync<PlayerProfile>(userId, Kind, subject, language, cancellationToken);
        if (stored is null) return null;

        var stats = await GetStatsAsync(userId, gameType, cancellationToken);
        if (stats is null) return null;
        var leakData = await GetLeaksAsync(userId, gameType, cancellationToken);
        return View(stored, Fingerprint(stats, leakData, language));
    }

    public async Task<ReportOutcome<PlayerProfile>> GenerateAsync(Guid userId, GameType gameType, string language, CancellationToken cancellationToken)
    {
        var subject = Subject(gameType);
        var stats = await GetStatsAsync(userId, gameType, cancellationToken);
        if (stats is null)
            return ReportOutcome<PlayerProfile>.Fail(CoachingFailure.NotFound);

        if (stats.Overall.Hands < MinHands)
            return ReportOutcome<PlayerProfile>.Fail(CoachingFailure.NotEnoughData);

        var leakData = await GetLeaksAsync(userId, gameType, cancellationToken);
        var fingerprint = Fingerprint(stats, leakData, language);
        var stored = await reports.FindAsync<PlayerProfile>(userId, Kind, subject, language, cancellationToken);
        if (stored is not null && stored.Fingerprint == fingerprint)
            return ReportOutcome<PlayerProfile>.Of(View(stored, fingerprint));

        var facts = BuildFacts(stats, leakData, gameType);
        var outcome = await guard.RunAsync(
            userId,
            AiTask.PlayerProfile,
            (model, ct) => model.ProfilePlayerAsync(new PlayerProfilePrompt(language, facts, gameType), ct),
            cancellationToken);
        if (outcome.Answer is not { } answer)
            return ReportOutcome<PlayerProfile>.Fail(outcome.Failure!.Value);

        var report = new StoredReport<PlayerProfile>(answer.Value, fingerprint, answer.Usage.Model, time.GetUtcNow());
        await reports.SaveAsync(userId, Kind, subject, language, report, cancellationToken);
        return ReportOutcome<PlayerProfile>.Of(View(report, fingerprint));
    }

    private async Task<StatisticsReport?> GetStatsAsync(Guid userId, GameType gameType, CancellationToken cancellationToken)
    {
        var filter = new StatisticsFilter(From: null, To: null, MinStackBigBlinds: null, MaxStackBigBlinds: null, GameType: gameType);
        var report = await statistics.GetAsync(userId, filter, cancellationToken);
        return report.Overall.Hands == 0 ? null : report;
    }

    private async Task<LeakData> GetLeaksAsync(Guid userId, GameType gameType, CancellationToken cancellationToken)
    {
        var analysis = await leaks.GetAsync(userId, format: null, from: null, to: null, cancellationToken, gameType: gameType);
        return new LeakData(
            analysis.Report.Leaks.Where(l => l.Confidence == LeakConfidence.Confirmed).ToList(),
            analysis.Report.Leaks.Where(l => l.Confidence == LeakConfidence.Possible).ToList());
    }

    internal sealed record LeakData(IReadOnlyList<Leak> Confirmed, IReadOnlyList<Leak> Possible);

    internal static string BuildFacts(StatisticsReport stats, LeakData leakData, GameType gameType)
    {
        string Pct(decimal? ratio) => ratio is { } r ? (r * 100m).ToString("0.#", CultureInfo.InvariantCulture) + " %" : "N/A";
        string Range(decimal min, decimal max) => $"{Pct(min)}–{Pct(max)}";
        var o = stats.Overall;
        var refs = gameType == GameType.CashGame ? ReferenceRanges.LowStakesCash : ReferenceRanges.LowStakesMtt;
        ReferenceRange? Ref(LeakStat stat) => refs.FirstOrDefault(r => r.Stat == stat && r.Position is null);

        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Player profile analysis — {o.Hands} hands ({stats.Format} tables).");
        text.AppendLine();

        text.AppendLine("Reference ranges for a solid regular at these stakes:");
        var globalStats = new (LeakStat Stat, string Label)[]
        {
            (LeakStat.Vpip, "VPIP"), (LeakStat.Pfr, "PFR"), (LeakStat.VpipPfrGap, "VPIP-PFR gap"),
            (LeakStat.ThreeBet, "3-bet"), (LeakStat.Steal, "Steal"), (LeakStat.Limp, "Limp"),
            (LeakStat.FoldToThreeBet, "Fold to 3-bet"),
            (LeakStat.CbetFlop, "C-bet flop"), (LeakStat.CbetTurn, "C-bet turn"),
            (LeakStat.CheckRaiseFlop, "Check-raise flop"), (LeakStat.PostflopAggression, "Postflop aggression"),
            (LeakStat.WentToShowdown, "WTSD"), (LeakStat.WonAtShowdown, "W$SD"),
            (LeakStat.WonWhenSawFlop, "Won when saw flop"),
        };
        foreach (var (stat, label) in globalStats)
        {
            if (Ref(stat) is { } r)
                text.AppendLine(CultureInfo.InvariantCulture, $"  {label}: {Range(r.Min, r.Max)}");
        }
        text.AppendLine();

        text.AppendLine("Preflop statistics:");
        text.AppendLine(CultureInfo.InvariantCulture, $"  VPIP: {Pct(o.Vpip.Rate)} ({o.Vpip.Made}/{o.Vpip.Opportunities})");
        text.AppendLine(CultureInfo.InvariantCulture, $"  PFR: {Pct(o.Pfr.Rate)} ({o.Pfr.Made}/{o.Pfr.Opportunities})");
        text.AppendLine(CultureInfo.InvariantCulture, $"  3-bet: {Pct(o.ThreeBet.Rate)} ({o.ThreeBet.Made}/{o.ThreeBet.Opportunities})");
        text.AppendLine(CultureInfo.InvariantCulture, $"  Steal: {Pct(o.Steal.Rate)} ({o.Steal.Made}/{o.Steal.Opportunities})");
        text.AppendLine(CultureInfo.InvariantCulture, $"  RFI: {Pct(o.Rfi.Rate)} ({o.Rfi.Made}/{o.Rfi.Opportunities})");
        text.AppendLine(CultureInfo.InvariantCulture, $"  Limp: {Pct(o.Limp.Rate)} ({o.Limp.Made}/{o.Limp.Opportunities})");
        text.AppendLine(CultureInfo.InvariantCulture, $"  Fold to 3-bet: {Pct(o.FoldToThreeBet.Rate)} ({o.FoldToThreeBet.Made}/{o.FoldToThreeBet.Opportunities})");
        text.AppendLine();
        text.AppendLine("Postflop statistics:");
        text.AppendLine(CultureInfo.InvariantCulture, $"  C-bet flop: {Pct(o.CbetFlop.Rate)} ({o.CbetFlop.Made}/{o.CbetFlop.Opportunities})");
        text.AppendLine(CultureInfo.InvariantCulture, $"  C-bet turn: {Pct(o.CbetTurn.Rate)} ({o.CbetTurn.Made}/{o.CbetTurn.Opportunities})");
        text.AppendLine(CultureInfo.InvariantCulture, $"  Check-raise flop: {Pct(o.CheckRaiseFlop.Rate)} ({o.CheckRaiseFlop.Made}/{o.CheckRaiseFlop.Opportunities})");
        text.AppendLine(CultureInfo.InvariantCulture, $"  Postflop aggression: {Pct(o.PostflopAggression.Rate)} ({o.PostflopAggression.Made}/{o.PostflopAggression.Opportunities})");
        text.AppendLine(CultureInfo.InvariantCulture, $"  WTSD: {Pct(o.WentToShowdown.Rate)} ({o.WentToShowdown.Made}/{o.WentToShowdown.Opportunities})");
        text.AppendLine(CultureInfo.InvariantCulture, $"  W$SD: {Pct(o.WonAtShowdown.Rate)} ({o.WonAtShowdown.Made}/{o.WonAtShowdown.Opportunities})");
        text.AppendLine(CultureInfo.InvariantCulture, $"  Won when saw flop: {Pct(o.WonWhenSawFlop.Rate)} ({o.WonWhenSawFlop.Made}/{o.WonWhenSawFlop.Opportunities})");
        text.AppendLine();
        if (o.BigBlindsPer100 is { } bb100)
            text.AppendLine(CultureInfo.InvariantCulture, $"Win rate: {bb100:0.00} BB/100.");

        if (leakData.Confirmed.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Confirmed leaks:");
            foreach (var l in leakData.Confirmed)
            {
                var pos = l.Position is { } p ? $" ({p})" : " (global)";
                var dir = l.Direction == LeakDirection.TooHigh ? "too high" : "too low";
                text.AppendLine(CultureInfo.InvariantCulture, $"  {l.Stat} {dir}{pos}: rate {Pct(l.Rate)}, expected {Pct(l.Range.Min)}–{Pct(l.Range.Max)}");
            }
        }

        if (leakData.Possible.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Possible leaks:");
            foreach (var l in leakData.Possible)
            {
                var pos = l.Position is { } p ? $" ({p})" : " (global)";
                var dir = l.Direction == LeakDirection.TooHigh ? "too high" : "too low";
                text.AppendLine(CultureInfo.InvariantCulture, $"  {l.Stat} {dir}{pos}: rate {Pct(l.Rate)}, expected {Pct(l.Range.Min)}–{Pct(l.Range.Max)}");
            }
        }

        return text.ToString();
    }

    internal static string Fingerprint(StatisticsReport stats, LeakData leakData, string language)
    {
        var o = stats.Overall;
        var key = string.Join(
            '|',
            Version.ToString(CultureInfo.InvariantCulture),
            stats.Format,
            o.Hands.ToString(CultureInfo.InvariantCulture),
            Round(o.Vpip.Rate), Round(o.Pfr.Rate), Round(o.ThreeBet.Rate),
            Round(o.Steal.Rate), Round(o.Limp.Rate), Round(o.PostflopAggression.Rate),
            leakData.Confirmed.Count.ToString(CultureInfo.InvariantCulture),
            leakData.Possible.Count.ToString(CultureInfo.InvariantCulture),
            language);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    private static string Round(decimal? rate) =>
        rate is { } r ? Math.Round(r * 100m).ToString(CultureInfo.InvariantCulture) : "-";

    private static string Subject(GameType gameType) =>
        gameType == GameType.Tournament ? "player-profile" : $"player-profile:{gameType}";

    private static ReportView<PlayerProfile> View(StoredReport<PlayerProfile> stored, string fingerprint) =>
        new(stored.Report, stored.Fingerprint != fingerprint, stored.Model, stored.CreatedAt);
}
