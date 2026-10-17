using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Api.Statistics;

internal static class TournamentSubFormatQuery
{
    /// <returns>False when a value is given but is not a valid TournamentSubFormat.</returns>
    public static bool TryParse(string? value, out TournamentSubFormat? subFormat)
    {
        subFormat = null;
        if (value is null)
        {
            return true;
        }

        if (!Enum.TryParse<TournamentSubFormat>(value, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed) || int.TryParse(value, out _))
        {
            return false;
        }

        subFormat = parsed;
        return true;
    }
}
