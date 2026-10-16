using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Application.Statistics;

/// <summary>A gap larger than this between two consecutive hands starts a new session.</summary>
public static class CashSessionGap
{
    public static readonly TimeSpan Max = TimeSpan.FromMinutes(30);
}

public sealed record CashSessionSummary(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int DurationMinutes,
    int Hands,
    string Stakes,
    long BigBlind,
    decimal NetBigBlinds,
    decimal? BigBlindsPer100,
    decimal NetMoney);

public sealed record CashSessionStackPoint(int Index, DateTimeOffset StartedAt, decimal StackInBigBlinds);

public sealed record CashSessionKeyMoment(
    int Index,
    Guid HandId,
    DateTimeOffset StartedAt,
    PokerPosition? Position,
    string? HeroCards,
    decimal StackInBigBlinds,
    decimal NetBigBlinds);

public sealed record CashSessionDetail(
    CashSessionSummary Summary,
    IReadOnlyList<CashSessionStackPoint> Stack,
    IReadOnlyList<CashSessionKeyMoment> KeyMoments,
    StatLine Stats,
    int PendingHands);

public sealed record CashSessionPage(IReadOnlyList<CashSessionSummary> Items, int Page, int PageSize, int TotalCount);

public interface ICashSessionReadStore
{
    /// <summary>All cash hand summaries for the user, sorted by start time ascending, within optional bounds.</summary>
    Task<IReadOnlyList<CashHandSummary>> ListSessionHandsAsync(
        Guid userId, DateTimeOffset? from, DateTimeOffset? to, int factsVersion, CancellationToken cancellationToken);

    Task<HeroStatCounts> GetCashSessionStatsAsync(
        Guid userId, DateTimeOffset from, DateTimeOffset to, int factsVersion, CancellationToken cancellationToken);

    Task<int> CountPendingCashAsync(Guid userId, int factsVersion, CancellationToken cancellationToken);
}

public sealed class CashSessionService(ICashSessionReadStore store)
{
    private const int MinKeyMomentBigBlinds = 10;
    private const int MaxKeyMoments = 8;

    public async Task<CashSessionPage> ListAsync(
        Guid userId, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken cancellationToken)
    {
        var hands = await store.ListSessionHandsAsync(userId, from, to, HeroHandFacts.Version, cancellationToken);
        var sessions = GroupSessions(hands);
        var total = sessions.Count;
        var items = sessions
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        return new CashSessionPage(items, page, pageSize, total);
    }

    public async Task<CashSessionDetail?> GetAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var hands = await store.ListSessionHandsAsync(userId, null, null, HeroHandFacts.Version, cancellationToken);
        var sessions = GroupHandsBySessions(hands);
        if (!sessions.TryGetValue(sessionId, out var sessionHands))
        {
            return null;
        }

        var from = sessionHands[0].StartedAt;
        var to = sessionHands[^1].StartedAt.AddSeconds(1);

        var stats = await store.GetCashSessionStatsAsync(userId, from, to, HeroHandFacts.Version, cancellationToken);
        var pending = await store.CountPendingCashAsync(userId, HeroHandFacts.Version, cancellationToken);

        var summary = SummariseSession(sessionId, sessionHands);

        var stack = sessionHands
            .Select((h, i) => new CashSessionStackPoint(i + 1, h.StartedAt, Math.Round(h.StackInBigBlinds, 1)))
            .ToList();

        var moments = SelectKeyMoments(sessionHands);

        return new CashSessionDetail(summary, stack, moments, StatLine.From(stats), pending);
    }

    internal static IReadOnlyList<CashSessionSummary> GroupSessions(IReadOnlyList<CashHandSummary> hands) =>
        GroupHandsBySessions(hands)
            .Select(kv => SummariseSession(kv.Key, kv.Value))
            .OrderByDescending(s => s.StartedAt)
            .ToList();

    private static Dictionary<Guid, List<CashHandSummary>> GroupHandsBySessions(IReadOnlyList<CashHandSummary> hands)
    {
        var sessions = new Dictionary<Guid, List<CashHandSummary>>();
        Guid currentId = Guid.Empty;

        for (var i = 0; i < hands.Count; i++)
        {
            var hand = hands[i];
            var isNewSession = i == 0
                || hand.StartedAt - hands[i - 1].StartedAt > CashSessionGap.Max;

            if (isNewSession)
            {
                currentId = hand.HandId;
                sessions[currentId] = [];
            }

            sessions[currentId].Add(hand);
        }

        return sessions;
    }

    private static CashSessionSummary SummariseSession(Guid id, List<CashHandSummary> hands)
    {
        var first = hands[0];
        var last = hands[^1];
        var netBb = hands.Sum(h => h.NetBigBlinds);
        var netMoney = Math.Round(hands.Sum(h => h.NetBigBlinds * h.BigBlind) / 100m, 2);
        var bigBlind = first.BigBlind;
        var stakes = $"{bigBlind / 200m:0.##}/{bigBlind / 100m:0.##}";
        var duration = (int)Math.Round((last.StartedAt - first.StartedAt).TotalMinutes);

        return new CashSessionSummary(
            id,
            first.StartedAt,
            last.StartedAt,
            duration,
            hands.Count,
            stakes,
            bigBlind,
            Math.Round(netBb, 2),
            hands.Count == 0 ? null : Math.Round(netBb / hands.Count * 100m, 2),
            netMoney);
    }

    private static List<CashSessionKeyMoment> SelectKeyMoments(List<CashHandSummary> hands) =>
        hands
            .Select((h, i) => (Index: i + 1, Hand: h))
            .Where(x => Math.Abs(x.Hand.NetBigBlinds) >= MinKeyMomentBigBlinds)
            .OrderByDescending(x => Math.Abs(x.Hand.NetBigBlinds))
            .Take(MaxKeyMoments)
            .OrderBy(x => x.Index)
            .Select(x => new CashSessionKeyMoment(
                x.Index,
                x.Hand.HandId,
                x.Hand.StartedAt,
                x.Hand.Position,
                x.Hand.HeroCards,
                Math.Round(x.Hand.StackInBigBlinds, 1),
                Math.Round(x.Hand.NetBigBlinds, 2)))
            .ToList();
}
