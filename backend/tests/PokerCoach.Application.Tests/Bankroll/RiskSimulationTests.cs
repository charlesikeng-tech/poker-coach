using PokerCoach.Domain.Bankroll;

namespace PokerCoach.Application.Tests.Bankroll;

public sealed class RiskSimulationTests
{
    private static readonly DateTimeOffset Day = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Roi_is_total_profit_over_total_cost_with_an_interval_around_it()
    {
        // 9 bust-outs and one 30x: +200 % on 10 tournaments, wildly uncertain.
        var results = Enumerable.Repeat(-1m, 9).Append(29m).Select(r => Money(10m, r * 10m)).ToList();

        var roi = RiskSimulation.Roi(results);

        Assert.Equal(10, roi.Tournaments);
        Assert.Equal(2m, roi.Mean);
        Assert.True(roi.Low < 0m, $"low {roi.Low}");
        Assert.True(roi.High > 4m, $"high {roi.High}");
    }

    [Fact]
    public void More_tournaments_narrow_the_interval()
    {
        var pattern = Enumerable.Repeat(-1m, 9).Append(12m).ToList();
        var few = RiskSimulation.Roi(Repeat(pattern, 10));
        var many = RiskSimulation.Roi(Repeat(pattern, 100));

        Assert.Equal(few.Mean, many.Mean);
        Assert.True(many.High - many.Low < few.High - few.Low);
    }

    [Fact]
    public void Freerolls_and_no_results_give_a_zero_estimate() =>
        Assert.Equal(new RoiEstimate(0, 0m, 0m, 0m), RiskSimulation.Roi([Money(0m, 5m)]));

    [Fact]
    public void No_simulation_on_a_thin_history() =>
        Assert.Null(RiskSimulation.Simulate(new Random(1), Repeat([-1m, 3m], (RiskSimulation.MinTournaments / 2) - 1), 1000m, 10m));

    [Fact]
    public void A_player_who_always_loses_goes_broke()
    {
        var risk = RiskSimulation.Simulate(new Random(1), Repeat([-1m], 60), 100m, 10m, ahead: 50)!;

        Assert.Equal(1m, risk.Ruin);
        Assert.Equal(1m, risk.HalfLoss);
        Assert.Equal(0m, risk.FinalMedian);
    }

    [Fact]
    public void A_player_who_always_wins_never_does()
    {
        var risk = RiskSimulation.Simulate(new Random(1), Repeat([1m], 60), 100m, 10m, ahead: 20)!;

        Assert.Equal(0m, risk.Ruin);
        Assert.Equal(300m, risk.FinalMedian);
        Assert.Equal(0m, risk.TypicalDownswing);
    }

    [Fact]
    public void Same_seed_same_outlook()
    {
        var history = Repeat([-1m, -1m, -1m, -1m, 5m], 20);

        Assert.Equal(
            RiskSimulation.Simulate(new Random(42), history, 500m, 5m),
            RiskSimulation.Simulate(new Random(42), history, 500m, 5m));
    }

    [Fact]
    public void A_thinner_bankroll_is_riskier()
    {
        var history = Repeat([-1m, -1m, -1m, -1m, -1m, -1m, -1m, -1m, 9.5m], 10);

        var thin = RiskSimulation.Simulate(new Random(7), history, 100m, 5m)!;
        var deep = RiskSimulation.Simulate(new Random(7), history, 1000m, 5m)!;

        Assert.True(thin.Ruin > deep.Ruin, $"thin {thin.Ruin}, deep {deep.Ruin}");
    }

    /// <param name="returns">Profit as a multiple of the cost, per tournament.</param>
    private static List<TournamentMoney> Repeat(IReadOnlyList<decimal> returns, int times) =>
        Enumerable.Range(0, times).SelectMany(_ => returns).Select(r => Money(10m, r * 10m)).ToList();

    private static TournamentMoney Money(decimal cost, decimal profit) => new(Day, cost, profit, "T");
}
