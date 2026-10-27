using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Application.Hands;

/// <summary>Reads aggregate opponent stats for the replayer and coaching flows.</summary>
public interface IOpponentStatsReader
{
    /// <summary>Stats for the given opponents, keyed by opponent name. Missing opponents are absent from the result.</summary>
    Task<IReadOnlyDictionary<string, OpponentStats>> GetAsync(Guid pokerAccountId, IReadOnlyCollection<string> opponentNames, CancellationToken cancellationToken);
}
