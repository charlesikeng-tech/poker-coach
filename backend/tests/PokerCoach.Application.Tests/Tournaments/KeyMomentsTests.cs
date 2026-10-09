using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Application.Tests.Tournaments;

public sealed class KeyMomentsTests
{
    [Fact]
    public void Ranks_by_share_of_the_stack_not_by_big_blinds_and_returns_play_order()
    {
        var moments = KeyMoments.Select(
        [
            // 20 BB won with 150 BB: 13 % of the stack, not a key moment.
            new KeyMomentCandidate(1, 30_000, 4_000, 20m, true, false),
            // Double-up at 15 BB preflop.
            new KeyMomentCandidate(2, 6_000, 6_000, 15m, false, true),
            // Lost half the stack after the flop.
            new KeyMomentCandidate(3, 12_000, -6_000, -10m, true, false),
            // Bust.
            new KeyMomentCandidate(4, 6_000, -6_000, -6m, false, false),
        ]);

        Assert.Equal(new[] { 2, 3, 4 }, moments.Select(m => m.Index));
        Assert.Equal(new[] { KeyMomentStage.Showdown, KeyMomentStage.Postflop, KeyMomentStage.Preflop }, moments.Select(m => m.Stage));
        Assert.Equal(new[] { 1m, -0.5m, -1m }, moments.Select(m => m.StackShare));
    }

    [Fact]
    public void Keeps_the_biggest_swings_when_there_are_too_many()
    {
        var hands = Enumerable.Range(1, 20)
            .Select(i => new KeyMomentCandidate(i, 10_000, i * 300, i, false, false))
            .ToList();

        var moments = KeyMoments.Select(hands);

        Assert.Equal(KeyMoments.MaxCount, moments.Count);
        Assert.Equal(Enumerable.Range(13, 8), moments.Select(m => m.Index));
    }

    [Fact]
    public void Ignores_hands_without_a_stack_or_without_a_swing()
    {
        Assert.Empty(KeyMoments.Select(
        [
            new KeyMomentCandidate(1, 0, 500, 1m, false, false),
            new KeyMomentCandidate(2, 5_000, 0, 0m, false, false),
        ]));
    }
}
