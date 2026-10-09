using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Api.Statistics;

/// <summary>Counts per table format (hands, or spots, depending on the endpoint).</summary>
public sealed record FormatCountsResponse(int SixMax, int FullRing);

/// <summary>The optional <c>format</c> query parameter shared by the statistics, leaks and ranges endpoints.</summary>
internal static class TableFormatQuery
{
    /// <returns>False when a value is given but is not sixMax or fullRing.</returns>
    public static bool TryParse(string? value, out TableFormat? format)
    {
        format = null;
        if (value is null)
        {
            return true;
        }

        if (!Enum.TryParse<TableFormat>(value, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed) || int.TryParse(value, out _))
        {
            return false;
        }

        format = parsed;
        return true;
    }
}
