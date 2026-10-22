using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Application.Ranges;

public sealed class CustomRangeService(ICustomRangeStore store)
{
    public Task<CustomOpeningRange?> GetOpeningAsync(
        Guid userId, TableFormat format, StackBand band, PokerPosition position, GameType gameType, CancellationToken cancellationToken) =>
        store.FindOpeningAsync(userId, format, band, position, gameType, cancellationToken);

    public async Task SaveOpeningAsync(
        Guid userId, TableFormat format, StackBand band, PokerPosition position, GameType gameType,
        IReadOnlySet<int> hands, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await store.FindOpeningAsync(userId, format, band, position, gameType, cancellationToken);
        if (existing is not null)
        {
            existing.Update(hands, now);
            await store.SaveOpeningAsync(existing, cancellationToken);
        }
        else
        {
            var range = CustomOpeningRange.Create(userId, format, band, position, gameType, hands, now);
            await store.SaveOpeningAsync(range, cancellationToken);
        }
    }

    public Task<bool> DeleteOpeningAsync(
        Guid userId, TableFormat format, StackBand band, PokerPosition position, GameType gameType, CancellationToken cancellationToken) =>
        store.DeleteOpeningAsync(userId, format, band, position, gameType, cancellationToken);

    public Task<IReadOnlyList<CustomOpeningRange>> ListOpeningsAsync(
        Guid userId, TableFormat format, StackBand band, GameType gameType, CancellationToken cancellationToken) =>
        store.ListOpeningsAsync(userId, format, band, gameType, cancellationToken);

    public Task<CustomDefenceRange?> GetDefenceAsync(
        Guid userId, TableFormat format, StackBand band,
        PokerPosition defender, PokerPosition opener, GameType gameType, CancellationToken cancellationToken) =>
        store.FindDefenceAsync(userId, format, band, defender, opener, gameType, cancellationToken);

    public async Task SaveDefenceAsync(
        Guid userId, TableFormat format, StackBand band,
        PokerPosition defender, PokerPosition opener, GameType gameType,
        IReadOnlySet<int> callHands, IReadOnlySet<int> threeBetHands,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await store.FindDefenceAsync(userId, format, band, defender, opener, gameType, cancellationToken);
        if (existing is not null)
        {
            existing.Update(callHands, threeBetHands, now);
            await store.SaveDefenceAsync(existing, cancellationToken);
        }
        else
        {
            var range = CustomDefenceRange.Create(userId, format, band, defender, opener, gameType, callHands, threeBetHands, now);
            await store.SaveDefenceAsync(range, cancellationToken);
        }
    }

    public Task<bool> DeleteDefenceAsync(
        Guid userId, TableFormat format, StackBand band,
        PokerPosition defender, PokerPosition opener, GameType gameType, CancellationToken cancellationToken) =>
        store.DeleteDefenceAsync(userId, format, band, defender, opener, gameType, cancellationToken);

    public Task<IReadOnlyList<CustomDefenceRange>> ListDefencesAsync(
        Guid userId, TableFormat format, StackBand band, GameType gameType, CancellationToken cancellationToken) =>
        store.ListDefencesAsync(userId, format, band, gameType, cancellationToken);
}
