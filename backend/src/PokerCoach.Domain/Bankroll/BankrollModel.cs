namespace PokerCoach.Domain.Bankroll;

/// <summary>Money moved in or out of the poker bankroll by the player. Values are stored: never renumber.</summary>
public enum BankrollMovementKind
{
    Deposit = 0,
    Withdrawal = 1,

    /// <summary>A correction: positive or negative (cash games, rakeback, another room…).</summary>
    Adjustment = 2,
}

/// <param name="Amount">Positive for deposits and withdrawals; signed for adjustments.</param>
public sealed record BankrollMovement(Guid Id, BankrollMovementKind Kind, decimal Amount, DateTimeOffset OccurredAt, string? Note)
{
    /// <summary>Effect on the bankroll: a withdrawal takes money out.</summary>
    public decimal Signed => Kind == BankrollMovementKind.Withdrawal ? -Amount : Amount;
}

/// <summary>How careful the player wants to be: the bankroll counted in average buy-ins.</summary>
public enum BuyInRule
{
    /// <summary>200 buy-ins: the usual advice for MTTs with big fields.</summary>
    Conservative = 0,

    /// <summary>100 buy-ins.</summary>
    Standard = 1,

    /// <summary>50 buy-ins: fast progression, real risk of busting.</summary>
    Aggressive = 2,
}

/// <param name="StartingAmount">Bankroll on <paramref name="StartedOn"/>; tournaments before it do not count.</param>
public sealed record BankrollSettings(decimal StartingAmount, DateTimeOffset StartedOn, BuyInRule Rule)
{
    public int BuyIns => Rule switch
    {
        BuyInRule.Conservative => 200,
        BuyInRule.Standard => 100,
        _ => 50,
    };

    /// <summary>Highest average buy-in the rule allows with this bankroll (never negative).</summary>
    public decimal MaxAverageBuyIn(decimal balance) => Math.Max(0m, Math.Round(balance / BuyIns, 2));
}

/// <summary>A tournament's money as the bankroll sees it: only tournaments with a known result.</summary>
public sealed record TournamentMoney(DateTimeOffset PlayedAt, decimal Cost, decimal Profit, string Name);

/// <summary>One step of the balance curve.</summary>
public sealed record BalancePoint(DateTimeOffset At, decimal Balance, decimal Change, BalanceEvent Event, string? Label);

public enum BalanceEvent
{
    Start,
    Tournament,
    Movement,
}

/// <summary>The bankroll over time: start, then every tournament result and movement in time order.</summary>
public static class BankrollLedger
{
    public static IReadOnlyList<BalancePoint> Curve(
        BankrollSettings settings,
        IEnumerable<TournamentMoney> tournaments,
        IEnumerable<BankrollMovement> movements)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var events = tournaments
            .Where(t => t.PlayedAt >= settings.StartedOn)
            .Select(t => (At: t.PlayedAt, Change: t.Profit, Event: BalanceEvent.Tournament, Label: (string?)t.Name))
            .Concat(movements
                .Where(m => m.OccurredAt >= settings.StartedOn)
                .Select(m => (At: m.OccurredAt, Change: m.Signed, Event: BalanceEvent.Movement, Label: m.Note)))
            .OrderBy(e => e.At)
            .ToList();

        var points = new List<BalancePoint>(events.Count + 1)
        {
            new(settings.StartedOn, settings.StartingAmount, 0m, BalanceEvent.Start, null),
        };
        var balance = settings.StartingAmount;
        foreach (var e in events)
        {
            balance += e.Change;
            points.Add(new BalancePoint(e.At, balance, e.Change, e.Event, e.Label));
        }

        return points;
    }
}
