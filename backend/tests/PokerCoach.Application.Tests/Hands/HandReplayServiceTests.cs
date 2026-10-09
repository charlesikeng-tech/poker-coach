using PokerCoach.Application.Hands;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Application.Tests.Hands;

public sealed class HandReplayServiceTests
{
    [Fact]
    public async Task Unknown_or_foreign_hand_is_null()
    {
        var service = new HandReplayService(new StubStore(null));

        Assert.Null(await service.GetAsync(Guid.NewGuid(), Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Seats_are_named_by_position_and_only_known_cards_are_given()
    {
        var replay = HandReplayService.Build(Source());

        Assert.Equal(new[] { 1, 2, 3, 4 }, replay.Seats.Select(s => s.SeatNumber));
        var hero = replay.Seats.Single(s => s.IsHero);
        Assert.Equal(PokerPosition.Button, hero.Position);
        Assert.Equal(new[] { "Ah", "Kd" }, hero.Cards!.Select(c => c.ToString()));

        // Shown at showdown: cards known. Folded: unknown, not empty.
        Assert.Equal(new[] { "Qs", "Qc" }, replay.Seats.Single(s => s.SeatNumber == 3).Cards!.Select(c => c.ToString()));
        Assert.Null(replay.Seats.Single(s => s.SeatNumber == 2).Cards);

        // Seated but not dealt in: shown at the table, no position.
        var away = replay.Seats.Single(s => s.SeatNumber == 4);
        Assert.False(away.IsDealt);
        Assert.Null(away.Position);

        Assert.Equal(4_400, replay.Seats.Single(s => s.SeatNumber == 3).Collected);
        Assert.Equal(new[] { 2, 3, 1, 2, 3, 1, 3 }, replay.Actions.Select(a => a.SeatNumber));
    }

    [Fact]
    public void Unreadable_hero_cards_are_unknown()
    {
        var replay = HandReplayService.Build(Source() with { HeroCards = "Ah?" });

        Assert.Null(replay.Seats.Single(s => s.IsHero).Cards);
    }

    private static HandReplaySource Source()
    {
        var hand = new HandForAnalysis(
            1,
            200,
            "Hero",
            [
                new SeatState(1, "Hero", 5_000),
                new SeatState(2, "Alice", 6_000),
                new SeatState(3, "Bob", 4_000),
                new SeatState(4, "Away", 3_000),
            ],
            [
                new HandAction(Street.Preflop, "Alice", ActionKind.PostSmallBlind, 100, false),
                new HandAction(Street.Preflop, "Bob", ActionKind.PostBigBlind, 200, false),
                new HandAction(Street.Preflop, "Hero", ActionKind.Raise, 400, false),
                new HandAction(Street.Preflop, "Alice", ActionKind.Fold, 0, false),
                new HandAction(Street.Preflop, "Bob", ActionKind.Raise, 3_800, true),
                new HandAction(Street.Preflop, "Hero", ActionKind.Call, 3_600, false),
                new HandAction(Street.Flop, "Bob", ActionKind.Check, 0, false),
            ],
            new Dictionary<string, long> { ["Bob"] = 4_400 });

        return new HandReplaySource(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "CASSIOPEIA",
            DateTimeOffset.UnixEpoch,
            3,
            100,
            200,
            null,
            6,
            "AhKd",
            hand,
            [],
            new Dictionary<string, IReadOnlyList<Card>> { ["Bob"] = [new Card(Rank.Queen, Suit.Spades), new Card(Rank.Queen, Suit.Clubs)] },
            12,
            80,
            Guid.NewGuid(),
            null);
    }

    private sealed class StubStore(HandReplaySource? source) : IHandReplayStore
    {
        public Task<HandReplaySource?> FindAsync(Guid userId, Guid handId, CancellationToken cancellationToken) =>
            Task.FromResult(source);
    }
}
