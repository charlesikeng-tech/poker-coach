using System.Text.RegularExpressions;

namespace PokerCoach.HandHistories.Winamax;

/// <summary>
/// Line grammar of Winamax files (see docs/research/winamax-format-notes.md). Digits are matched with
/// [0-9], not \d, which would also accept non-ASCII digits. Player names are never matched by regex in
/// action lines: they are matched against the seated players instead (names can contain spaces).
/// </summary>
internal static partial class WinamaxPatterns
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture;

    [GeneratedRegex(
        @"^Winamax Poker - Tournament ""(?<name>.+)"" buyIn: (?<buyIn>.+?) level: (?<level>[0-9]+) - HandId: #(?<id>[0-9]+-[0-9]+-[0-9]+) - Holdem no limit \((?:(?<ante>[0-9]+)/)?(?<sb>[0-9]+)/(?<bb>[0-9]+)\) - (?<date>[0-9]{4}/[0-9]{2}/[0-9]{2} [0-9]{2}:[0-9]{2}:[0-9]{2}) UTC$",
        Options)]
    internal static partial Regex HandHeader();

    [GeneratedRegex(@"^(?<amount>[0-9]+(?:\.[0-9]+)?)€ \+ (?<fee>[0-9]+(?:\.[0-9]+)?)€$", Options)]
    internal static partial Regex HandBuyIn();

    [GeneratedRegex(
        @"^Table: '(?<table>.+)' (?<maxSeats>[0-9]+)-max \((?<money>real money|play money)\) Seat #(?<button>[0-9]+) is the button$",
        Options)]
    internal static partial Regex TableLine();

    [GeneratedRegex(@"\((?<id>[0-9]+)\)#[0-9]+$", Options)]
    internal static partial Regex TournamentIdInTableName();

    [GeneratedRegex(
        @"^Seat (?<seat>[0-9]+): (?<name>.+) \((?<stack>[0-9]+)(?:, (?<bounty>[0-9]+(?:\.[0-9]+)?)€ bounty)?\)$",
        Options)]
    internal static partial Regex SeatLine();

    [GeneratedRegex(@"^\*\*\* (?<street>FLOP|TURN|RIVER) \*\*\* (?:\[[^\]]+\])*\[(?<cards>[^\]]+)\]$", Options)]
    internal static partial Regex StreetMarker();

    [GeneratedRegex(@"^posts (?<kind>ante|small blind|big blind) (?<amount>[0-9]+)(?<allIn> and is all-in)?$", Options)]
    internal static partial Regex Post();

    [GeneratedRegex(@"^(?<kind>calls|bets) (?<amount>[0-9]+)(?<allIn> and is all-in)?$", Options)]
    internal static partial Regex CallOrBet();

    [GeneratedRegex(@"^raises (?<by>[0-9]+) to (?<to>[0-9]+)(?<allIn> and is all-in)?$", Options)]
    internal static partial Regex Raise();

    [GeneratedRegex(@"^shows \[(?<cards>[^\]]+)\](?: \((?<description>.+)\))?$", Options)]
    internal static partial Regex Shows();

    [GeneratedRegex(@"^collected (?<amount>[0-9]+) from (?:(?<main>main pot)|side pot (?<side>[0-9]+)|pot)$", Options)]
    internal static partial Regex Collected();

    [GeneratedRegex(@"^Total pot (?<amount>[0-9]+) \| No rake$", Options)]
    internal static partial Regex TotalPot();

    [GeneratedRegex(@"^Board: \[(?<cards>[^\]]+)\]$", Options)]
    internal static partial Regex SummaryBoard();

    [GeneratedRegex(@"^Seat [0-9]+: .+$", Options)]
    internal static partial Regex SummarySeat();

    // Tournament summary file.

    [GeneratedRegex(@"^Winamax Poker - Tournament summary : (?<name>.+)\((?<id>[0-9]+)\)$", Options)]
    internal static partial Regex SummaryHeader();

    [GeneratedRegex(@"^(?<key>Player|Buy-In|Registered players|Mode|Type|Speed|Flight ID|Prizepool) : (?<value>.+)$", Options)]
    internal static partial Regex SummaryField();

    [GeneratedRegex(@"^Tournament started (?<date>[0-9]{4}/[0-9]{2}/[0-9]{2} [0-9]{2}:[0-9]{2}:[0-9]{2}) UTC$", Options)]
    internal static partial Regex SummaryStarted();

    [GeneratedRegex(@"^You played (?:(?<hours>[0-9]+)h )?(?:(?<minutes>[0-9]+)min )?(?<seconds>[0-9]+)s$", Options)]
    internal static partial Regex SummaryPlayed();

    [GeneratedRegex(@"^You finished in (?<position>[0-9]+)(?:st|nd|rd|th) place$", Options)]
    internal static partial Regex SummaryFinished();

    [GeneratedRegex(
        @"^You won (?<prize>[0-9]+(?:\.[0-9]+)?)€(?: \+ Bounty (?<bounty>[0-9]+(?:\.[0-9]+)?)€)?$",
        Options)]
    internal static partial Regex SummaryWon();
}
