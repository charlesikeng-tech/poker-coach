using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Application.Opponents;

public sealed record OpponentListItem(
    Guid Id,
    string OpponentName,
    PokerRoom Room,
    string ScreenName,
    int Hands,
    PlayerType Type,
    decimal VpipRate,
    decimal PfrRate,
    decimal ThreeBetRate,
    string? Note,
    IReadOnlyList<string> Tags,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

public sealed record OpponentListResult(IReadOnlyList<OpponentListItem> Items, int TotalCount);

public sealed record OpponentDetail(
    Guid Id,
    string OpponentName,
    PokerRoom Room,
    string ScreenName,
    OpponentStats Stats,
    PlayerType Type,
    string? Note,
    IReadOnlyList<string> Tags,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    int DistinctTournaments);

public enum OpponentSort { Hands, Name, LastSeen, Vpip }

public interface IOpponentStore
{
    Task<OpponentListResult> ListAsync(Guid userId, string? search, OpponentSort sort, bool descending, int page, int pageSize, CancellationToken cancellationToken);
    Task<OpponentDetail?> GetAsync(Guid userId, Guid opponentId, CancellationToken cancellationToken);
    Task<bool> SetNoteAsync(Guid userId, Guid opponentId, string? note, CancellationToken cancellationToken);
    Task<bool> SetTagsAsync(Guid userId, Guid opponentId, IReadOnlyList<string> tags, CancellationToken cancellationToken);
}

public sealed class OpponentService(IOpponentStore store)
{
    public Task<OpponentListResult> ListAsync(Guid userId, string? search, OpponentSort sort, bool descending, int page, int pageSize, CancellationToken cancellationToken) =>
        store.ListAsync(userId, search, sort, descending, page, pageSize, cancellationToken);

    public Task<OpponentDetail?> GetAsync(Guid userId, Guid opponentId, CancellationToken cancellationToken) =>
        store.GetAsync(userId, opponentId, cancellationToken);

    public Task<bool> SetNoteAsync(Guid userId, Guid opponentId, string? note, CancellationToken cancellationToken) =>
        store.SetNoteAsync(userId, opponentId, note, cancellationToken);

    public Task<bool> SetTagsAsync(Guid userId, Guid opponentId, IReadOnlyList<string> tags, CancellationToken cancellationToken) =>
        store.SetTagsAsync(userId, opponentId, tags, cancellationToken);
}
