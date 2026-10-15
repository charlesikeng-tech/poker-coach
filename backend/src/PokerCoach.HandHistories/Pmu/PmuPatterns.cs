using System.Text.RegularExpressions;

namespace PokerCoach.HandHistories.Pmu;

internal static partial class PmuPatterns
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture;

    [GeneratedRegex(@"^GAME #(?<id>\d+) .+Texas Hold'em NL €(?<sb>[0-9]+(?:\.[0-9]+)?)/€(?<bb>[0-9]+(?:\.[0-9]+)?) (?<date>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})/(?<tz>\w+)$", Options)]
    internal static partial Regex HeaderLine();

    [GeneratedRegex(@"^Table Info: Size: (?<size>\d+), Blinds: .+$", Options)]
    internal static partial Regex TableInfoLine();

    [GeneratedRegex(@"^Table (?:\[.+?\]\s+)?(?<name>.+?), (?<id>\d+)$", Options)]
    internal static partial Regex TableLine();

    [GeneratedRegex(@"^Seat (?<seat>\d+): (?<name>.+?) \(€(?<stack>[0-9]+(?:\.[0-9]+)?) in chips\)(?<dealer>\s+DEALER)?$", Options)]
    internal static partial Regex SeatLine();

    [GeneratedRegex(@"^Dealt to (?<name>.+?) \[(?<cards>[^\]]+)\]$", Options)]
    internal static partial Regex DealtTo();

    [GeneratedRegex(@"^\*\*\* (?<street>FLOP|TURN|RIVER) \*\*\* \[(?<cards>[^\]]+)\]$", Options)]
    internal static partial Regex StreetMarker();

    [GeneratedRegex(@"^(?<name>.+?): Post (?<kind>SB|BB) €(?<amount>[0-9]+(?:\.[0-9]+)?)$", Options)]
    internal static partial Regex Post();

    [GeneratedRegex(@"^(?<name>.+?): Fold$", Options)]
    internal static partial Regex Fold();

    [GeneratedRegex(@"^(?<name>.+?): Check$", Options)]
    internal static partial Regex Check();

    [GeneratedRegex(@"^(?<name>.+?): Call €(?<amount>[0-9]+(?:\.[0-9]+)?)$", Options)]
    internal static partial Regex Call();

    [GeneratedRegex(@"^(?<name>.+?): Bet €(?<amount>[0-9]+(?:\.[0-9]+)?)$", Options)]
    internal static partial Regex Bet();

    [GeneratedRegex(@"^(?<name>.+?): Raise \(NF\) €(?<amount>[0-9]+(?:\.[0-9]+)?)$", Options)]
    internal static partial Regex Raise();

    [GeneratedRegex(@"^(?<name>.+?): Allin €(?<amount>[0-9]+(?:\.[0-9]+)?)$", Options)]
    internal static partial Regex Allin();

    [GeneratedRegex(@"^Total pot €(?<pot>[0-9]+(?:\.[0-9]+)?) Rake €(?<rake>[0-9]+(?:\.[0-9]+)?)$", Options)]
    internal static partial Regex TotalPot();

    [GeneratedRegex(@"^(?<name>.+?): Shows \[(?<cards>[^\]]+)\] (?<desc>.+)$", Options)]
    internal static partial Regex Shows();

    [GeneratedRegex(@"^(?<name>.+?): Mucks \[.+\]", Options)]
    internal static partial Regex Mucks();

    [GeneratedRegex(@"^(?<name>.+?): wins €(?<amount>[0-9]+(?:\.[0-9]+)?)$", Options)]
    internal static partial Regex Wins();
}
