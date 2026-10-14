using PokerCoach.Domain.Poker;

namespace PokerCoach.Api.Statistics;

internal static class GameTypeQuery
{
    /// <returns>False when a value is given but is not tournament or cashGame.</returns>
    public static bool TryParse(string? value, out GameType? gameType)
    {
        gameType = null;
        if (value is null)
        {
            return true;
        }

        if (!Enum.TryParse<GameType>(value, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed) || int.TryParse(value, out _))
        {
            return false;
        }

        gameType = parsed;
        return true;
    }
}
