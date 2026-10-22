using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Application.Ranges;

public interface ICustomRangeStore
{
    Task<CustomOpeningRange?> FindOpeningAsync(Guid userId, TableFormat format, StackBand band, PokerPosition position, GameType gameType, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomOpeningRange>> ListOpeningsAsync(Guid userId, TableFormat format, StackBand band, GameType gameType, CancellationToken cancellationToken);
    Task SaveOpeningAsync(CustomOpeningRange range, CancellationToken cancellationToken);
    Task<bool> DeleteOpeningAsync(Guid userId, TableFormat format, StackBand band, PokerPosition position, GameType gameType, CancellationToken cancellationToken);

    Task<CustomDefenceRange?> FindDefenceAsync(Guid userId, TableFormat format, StackBand band, PokerPosition defender, PokerPosition opener, GameType gameType, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomDefenceRange>> ListDefencesAsync(Guid userId, TableFormat format, StackBand band, GameType gameType, CancellationToken cancellationToken);
    Task SaveDefenceAsync(CustomDefenceRange range, CancellationToken cancellationToken);
    Task<bool> DeleteDefenceAsync(Guid userId, TableFormat format, StackBand band, PokerPosition defender, PokerPosition opener, GameType gameType, CancellationToken cancellationToken);
}
