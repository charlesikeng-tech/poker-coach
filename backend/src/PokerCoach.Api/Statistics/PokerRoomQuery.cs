using PokerCoach.Domain.Poker;

namespace PokerCoach.Api.Statistics;

internal static class PokerRoomQuery
{
    /// <returns>False when a value is given but does not match a known PokerRoom name.</returns>
    public static bool TryParse(string? value, out PokerRoom? room)
    {
        room = null;
        if (value is null)
        {
            return true;
        }

        if (!Enum.TryParse<PokerRoom>(value, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed) || int.TryParse(value, out _))
        {
            return false;
        }

        room = parsed;
        return true;
    }
}
