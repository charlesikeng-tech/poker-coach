using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Application.Hands;

/// <summary>Reads aggregate opponent stats for the replayer and coaching flows.</summary>
public interface IOpponentStatsReader
{
    /// <summary>Stats for the given opponents, keyed by opponent name. Missing opponents are absent from the result.</summary>
    Task<IReadOnlyDictionary<string, OpponentStats>> GetAsync(Guid pokerAccountId, IReadOnlyCollection<string> opponentNames, CancellationToken cancellationToken);

    /// <summary>Stats across all poker accounts of the user. Used by coaching services that work at user level.</summary>
    Task<IReadOnlyDictionary<string, OpponentStats>> GetByUserAsync(Guid userId, IReadOnlyCollection<string> opponentNames, CancellationToken cancellationToken);

    /// <summary>Record IDs of opponents for one poker account, keyed by name. Used by the replayer to build profile links.</summary>
    Task<IReadOnlyDictionary<string, Guid>> GetIdsAsync(Guid pokerAccountId, IReadOnlyCollection<string> opponentNames, CancellationToken cancellationToken);
}
