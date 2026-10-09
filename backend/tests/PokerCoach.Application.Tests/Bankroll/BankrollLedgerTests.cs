using PokerCoach.Domain.Bankroll;

namespace PokerCoach.Application.Tests.Bankroll;

public sealed class BankrollLedgerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Curve_starts_at_the_starting_amount_and_follows_time_order()
    {
        var settings = new BankrollSettings(500m, Start, BuyInRule.Standard);
        var tournaments = new[]
        {
            new TournamentMoney(Start.AddDays(3), 10m, 40m, "LATE"),
            new TournamentMoney(Start.AddDays(1), 10m, -10m, "EARLY"),
        };
        var movements = new[]
        {
            new BankrollMovement(Guid.NewGuid(), BankrollMovementKind.Withdrawal, 100m, Start.AddDays(2), "cash out"),
        };

        var curve = BankrollLedger.Curve(settings, tournaments, movements);

        Assert.Equal(new[] { 500m, 490m, 390m, 430m }, curve.Select(p => p.Balance));
        Assert.Equal(new[] { BalanceEvent.Start, BalanceEvent.Tournament, BalanceEvent.Movement, BalanceEvent.Tournament }, curve.Select(p => p.Event));
        Assert.Equal("cash out", curve[2].Label);
    }

    [Fact]
    public void What_happened_before_the_start_date_does_not_count()
    {
        var settings = new BankrollSettings(200m, Start, BuyInRule.Standard);

        var curve = BankrollLedger.Curve(
            settings,
            [new TournamentMoney(Start.AddDays(-1), 5m, 100m, "BEFORE")],
            [new BankrollMovement(Guid.NewGuid(), BankrollMovementKind.Deposit, 50m, Start.AddHours(-1), null)]);

        Assert.Single(curve);
        Assert.Equal(200m, curve[0].Balance);
    }

    [Fact]
    public void An_adjustment_keeps_its_sign()
    {
        Assert.Equal(-12.5m, new BankrollMovement(Guid.NewGuid(), BankrollMovementKind.Adjustment, -12.5m, Start, null).Signed);
        Assert.Equal(-30m, new BankrollMovement(Guid.NewGuid(), BankrollMovementKind.Withdrawal, 30m, Start, null).Signed);
    }

    [Theory]
    [InlineData(BuyInRule.Conservative, 1000, 5)]
    [InlineData(BuyInRule.Standard, 1000, 10)]
    [InlineData(BuyInRule.Aggressive, 1000, 20)]
    [InlineData(BuyInRule.Standard, -50, 0)]
    public void Limit_is_the_balance_over_the_rule_buy_ins(BuyInRule rule, int balance, int expected) =>
        Assert.Equal(expected, new BankrollSettings(0m, Start, rule).MaxAverageBuyIn(balance));
}
