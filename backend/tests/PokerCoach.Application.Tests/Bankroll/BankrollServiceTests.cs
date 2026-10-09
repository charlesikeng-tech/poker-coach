using PokerCoach.Application.Bankroll;
using PokerCoach.Application.Tournaments;
using PokerCoach.Domain.Bankroll;
using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Application.Tests.Bankroll;

public sealed class BankrollServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Start = Now.AddDays(-60);

    [Fact]
    public async Task Nothing_until_the_bankroll_is_set_up()
    {
        var service = Service(new InMemoryBankrollStore(), []);

        Assert.Null(await service.GetAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Balance_adds_results_and_movements_since_the_start_only()
    {
        var store = new InMemoryBankrollStore { Settings = new BankrollSettings(300m, Start, BuyInRule.Standard) };
        store.Movements.Add(new BankrollMovement(Guid.NewGuid(), BankrollMovementKind.Deposit, 100m, Start.AddDays(1), null));
        store.Movements.Add(new BankrollMovement(Guid.NewGuid(), BankrollMovementKind.Withdrawal, 50m, Start.AddDays(-5), null));
        var service = Service(store, [
            Facts("BEFORE", Start.AddDays(-10), 10m, [new EntryOutcome(1, 500m, null)]),
            Facts("WIN", Start.AddDays(2), 10m, [new EntryOutcome(2, 60m, null)]),
            Facts("BUST", Start.AddDays(3), 10m, [new EntryOutcome(400, null, null)]),
            Facts("NO SUMMARY", Start.AddDays(4), 10m, []),
        ]);

        var overview = (await service.GetAsync(Guid.NewGuid(), TestContext.Current.CancellationToken))!;

        Assert.Equal(40m, overview.TournamentProfit);
        Assert.Equal(100m, overview.NetDeposits);
        Assert.Equal(440m, overview.Balance);
        Assert.Equal(2, overview.Tournaments);
        Assert.Equal(1, overview.UnknownResults);
        Assert.Equal(4.4m, overview.Limit.MaxAverageBuyIn);

        // Skill does not restart with the bankroll: ROI looks at the whole history.
        Assert.Equal(3, overview.Roi.Tournaments);
        Assert.Null(overview.Risk);
    }

    [Fact]
    public async Task Flags_recent_tournaments_above_the_limit()
    {
        var store = new InMemoryBankrollStore { Settings = new BankrollSettings(1000m, Start, BuyInRule.Standard) };
        var service = Service(store, [
            Facts("OLD BIG ONE", Now.AddDays(-40), 50m, [new EntryOutcome(300, null, null)]),
            Facts("BIG ONE", Now.AddDays(-2), 20m, [new EntryOutcome(300, null, null)]),
            Facts("SMALL", Now.AddDays(-1), 5m, [new EntryOutcome(300, null, null)]),
        ]);

        var overview = (await service.GetAsync(Guid.NewGuid(), TestContext.Current.CancellationToken))!;

        Assert.Equal(9.25m, overview.Limit.MaxAverageBuyIn);
        Assert.Equal(1, overview.Limit.AboveLimitRecently);
        Assert.Equal(2, overview.Limit.PlayedRecently);
        Assert.Equal(25m, overview.Limit.RecentAverageBuyIn);
    }

    [Fact]
    public async Task Risk_appears_with_enough_history_and_does_not_move_between_calls()
    {
        var store = new InMemoryBankrollStore { Settings = new BankrollSettings(500m, Start, BuyInRule.Standard) };
        var facts = Enumerable.Range(0, 60)
            .Select(i => Facts($"T{i}", Start.AddHours(i + 1), 5m, i % 6 == 0 ? [new EntryOutcome(3, 30m, null)] : [new EntryOutcome(300, null, null)]))
            .ToArray();
        var service = Service(store, facts);
        var userId = Guid.NewGuid();

        var first = (await service.GetAsync(userId, TestContext.Current.CancellationToken))!;
        var second = (await service.GetAsync(userId, TestContext.Current.CancellationToken))!;

        Assert.NotNull(first.Risk);
        Assert.Equal(first.Risk, second.Risk);
    }

    [Fact]
    public async Task A_movement_needs_a_bankroll_and_a_date_after_its_start()
    {
        var store = new InMemoryBankrollStore();
        var service = Service(store, []);
        var userId = Guid.NewGuid();

        var (missing, _) = await service.AddMovementAsync(userId, BankrollMovementKind.Deposit, 10m, Now, null, TestContext.Current.CancellationToken);
        store.Settings = new BankrollSettings(100m, Start, BuyInRule.Standard);
        var (early, _) = await service.AddMovementAsync(userId, BankrollMovementKind.Deposit, 10m, Start.AddDays(-1), null, TestContext.Current.CancellationToken);
        var (added, movement) = await service.AddMovementAsync(userId, BankrollMovementKind.Deposit, 10.004m, Now, "  reload  ", TestContext.Current.CancellationToken);

        Assert.Equal(MovementOutcome.NotConfigured, missing);
        Assert.Equal(MovementOutcome.BeforeStart, early);
        Assert.Equal(MovementOutcome.Added, added);
        Assert.Equal(10m, movement!.Amount);
        Assert.Equal("reload", movement.Note);
        Assert.Single(store.Movements);
    }

    private static BankrollService Service(InMemoryBankrollStore store, TournamentFacts[] facts) =>
        new(store, new StubTournaments(facts), new FixedTime(Now));

    /// <summary>Fee-free buy-in: the buy-in is the amount given.</summary>
    private static TournamentFacts Facts(string name, DateTimeOffset startedAt, decimal buyIn, IReadOnlyList<EntryOutcome> entries) =>
        new(Guid.NewGuid(), name, startedAt, "EUR", null, null, buyIn, 0m, 100, 0, entries);

    private sealed class StubTournaments(TournamentFacts[] facts) : ITournamentReadStore
    {
        public Task<IReadOnlyList<TournamentFacts>> ListAsync(Guid userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TournamentFacts>>(facts.OrderByDescending(f => f.StartedAt).ToList());
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InMemoryBankrollStore : IBankrollStore
    {
        public BankrollSettings? Settings { get; set; }

        public List<BankrollMovement> Movements { get; } = [];

        public Task<BankrollSettings?> GetSettingsAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Settings);

        public Task SaveSettingsAsync(Guid userId, BankrollSettings settings, CancellationToken cancellationToken)
        {
            Settings = settings;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BankrollMovement>> ListMovementsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BankrollMovement>>(Movements.OrderByDescending(m => m.OccurredAt).ToList());

        public Task AddMovementAsync(Guid userId, BankrollMovement movement, CancellationToken cancellationToken)
        {
            Movements.Add(movement);
            return Task.CompletedTask;
        }

        public Task<bool> DeleteMovementAsync(Guid userId, Guid movementId, CancellationToken cancellationToken) =>
            Task.FromResult(Movements.RemoveAll(m => m.Id == movementId) > 0);
    }
}
