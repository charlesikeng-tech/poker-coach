using System.Text.RegularExpressions;

namespace PokerCoach.HandHistories.Betclic;

internal static partial class BetclicPatterns
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture;

    [GeneratedRegex(@"^Seat (?<seat>[0-9]+): (?<name>.+?) \(€(?<stack>[0-9]+(?:\.[0-9]+)?)\)(?<tags> \[.+\])?$", Options)]
    internal static partial Regex SeatLine();

    [GeneratedRegex(@"^\*\*\* (?<street>FLOP|TURN|RIVER) \*\*\* \[(?<cards>[^\]]+)\]$", Options)]
    internal static partial Regex StreetMarker();

    [GeneratedRegex(@"^[0-9]{2}:[0-9]{2}:[0-9]{2} - (?<name>.+?): (?<action>.+)$", Options)]
    internal static partial Regex ActionLine();

    [GeneratedRegex(@"^Posts (?<kind>SB|BB) €(?<amount>[0-9]+(?:\.[0-9]+)?)$", Options)]
    internal static partial Regex Post();

    [GeneratedRegex(@"^Folds$", Options)]
    internal static partial Regex Fold();

    [GeneratedRegex(@"^Checks$", Options)]
    internal static partial Regex Check();

    [GeneratedRegex(@"^Calls €(?<amount>[0-9]+(?:\.[0-9]+)?)(?<allIn> and is all-in)?$", Options)]
    internal static partial Regex Call();

    [GeneratedRegex(@"^Bets €(?<amount>[0-9]+(?:\.[0-9]+)?)(?<allIn> and is all-in)?$", Options)]
    internal static partial Regex Bet();

    [GeneratedRegex(@"^Raises to €(?<to>[0-9]+(?:\.[0-9]+)?)(?<allIn> and is all-in)?$", Options)]
    internal static partial Regex Raise();

    [GeneratedRegex(@"^(?<name>.+?) shows \[(?<cards>[^\]]+)\] \((?<description>.+?)\) \[.+\]$", Options)]
    internal static partial Regex Shows();

    [GeneratedRegex(@"^(?<name>.+?) wins (?:main pot|(?<sideOrd>[0-9]+)(?:st|nd|rd|th) side pot) of €(?<amount>[0-9]+(?:\.[0-9]+)?)$", Options)]
    internal static partial Regex Wins();

    // --- Tournament variants (chip amounts without € prefix) ---

    [GeneratedRegex(@"^Seat (?<seat>[0-9]+): (?<name>.+?) \((?<stack>[0-9]+)(?:, €(?<bounty>[0-9]+(?:\.[0-9]+)?) bounty)?\)(?<tags> \[.+\])?$", Options)]
    internal static partial Regex TournamentSeatLine();

    [GeneratedRegex(@"^Posts (?<kind>SB|BB) (?<amount>[0-9]+)(?<allIn> and is all-in)?$", Options)]
    internal static partial Regex TournamentPost();

    [GeneratedRegex(@"^Calls (?<amount>[0-9]+)(?<allIn> and is all-in)?$", Options)]
    internal static partial Regex TournamentCall();

    [GeneratedRegex(@"^Bets (?<amount>[0-9]+)(?<allIn> and is all-in)?$", Options)]
    internal static partial Regex TournamentBet();

    [GeneratedRegex(@"^Raises to (?<to>[0-9]+)(?<allIn> and is all-in)?$", Options)]
    internal static partial Regex TournamentRaise();

    [GeneratedRegex(@"^(?<name>.+?) wins (?:main pot|(?<sideOrd>[0-9]+)(?:st|nd|rd|th) side pot) of (?<amount>[0-9]+)$", Options)]
    internal static partial Regex TournamentWins();

    [GeneratedRegex(@"^(?<name>.+) finished (?<position>[0-9]+)(?:st|nd|rd|th)$", Options)]
    internal static partial Regex FinishedLine();

    [GeneratedRegex(@"^(?<winner>.+?) wins €(?<amount>[0-9]+(?:\.[0-9]+)?) for eliminating (?<eliminated>.+?) \(x[0-9]+(?:\.[0-9]+)?\),.+$", Options)]
    internal static partial Regex BountyLine();
}
