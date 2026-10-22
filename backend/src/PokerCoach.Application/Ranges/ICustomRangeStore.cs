using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Application.Ranges;

public interface ICustomRangeStore
{
    Task<CustomOpeningRange?> FindOpeningAsync(Guid userId, TableFormat format, StackBand band, PokerPosition position, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomOpeningRange>> ListOpeningsAsync(Guid userId, TableFormat format, StackBand band, CancellationToken cancellationToken);
    Task SaveOpeningAsync(CustomOpeningRange range, CancellationToken cancellationToken);
    Task<bool> DeleteOpeningAsync(Guid userId, TableFormat format, StackBand band, PokerPosition position, CancellationToken cancellationToken);

    Task<CustomDefenceRange?> FindDefenceAsync(Guid userId, TableFormat format, StackBand band, PokerPosition defender, PokerPosition opener, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomDefenceRange>> ListDefencesAsync(Guid userId, TableFormat format, StackBand band, CancellationToken cancellationToken);
    Task SaveDefenceAsync(CustomDefenceRange range, CancellationToken cancellationToken);
    Task<bool> DeleteDefenceAsync(Guid userId, TableFormat format, StackBand band, PokerPosition defender, PokerPosition opener, CancellationToken cancellationToken);
}
