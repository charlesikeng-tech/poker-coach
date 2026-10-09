using Microsoft.Extensions.Time.Testing;
using PokerCoach.Application.Leaks;
using PokerCoach.Application.Statistics;
using PokerCoach.Application.Training;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Training;

namespace PokerCoach.Application.Tests.Training;

public sealed class OpeningDrillTests
{
    [Fact]
    public void The_answer_key_is_the_reference_range()
    {
        var ako = RangeNotation.Parse("AKo").Single();
        var seventyTwo = RangeNotation.Parse("72o").Single();

        Assert.Equal(DrillAnswer.Raise, OpeningDrill.Expected(new DrillItem(TableFormat.SixMax, StackBand.Mid, PokerPosition.Utg, ako)));
        Assert.Equal(DrillAnswer.Fold, OpeningDrill.Expected(new DrillItem(TableFormat.SixMax, StackBand.Mid, PokerPosition.Button, seventyTwo)));
    }

    [Fact]
    public void Dealt_spots_stay_in_the_seats_and_band_with_cards_of_the_hand()
    {
        var random = new Random(42);
        for (var i = 0; i < 500; i++)
        {
            var spot = OpeningDrill.Deal(random, TableFormat.SixMax, StackBand.Short, [PokerPosition.Button, PokerPosition.Cutoff]);

            Assert.Contains(spot.Item.Position, new[] { PokerPosition.Button, PokerPosition.Cutoff });
            Assert.InRange(spot.StackBigBlinds, 15m, 25m);
            Assert.Equal(spot.Item.Hand, HandClass.Of(spot.First, spot.Second));
            Assert.NotEqual(spot.First, spot.Second);
        }
    }

    [Fact]
    public void The_boundary_holds_hands_on_both_sides_of_the_edge()
    {
        var range = ReferenceOpeningRanges.For(StackBand.Mid, TableFormat.SixMax, PokerPosition.Button)!;
        var boundary = OpeningDrill.Boundary(range);

        Assert.Contains(boundary, range.Contains);
        Assert.Contains(boundary, h => !range.Contains(h));
        Assert.DoesNotContain(RangeNotation.Parse("AA").Single(), boundary);
    }

    [Fact]
    public void A_missed_hand_is_due_until_answered_right()
    {
        var item = new DrillItem(TableFormat.SixMax, StackBand.Mid, PokerPosition.Button, RangeNotation.Parse("K8o").Single());
        var other = item with { Hand = RangeNotation.Parse("Q9o").Single() };
        var now = DateTimeOffset.UnixEpoch;

        var missed = OpeningTrainingService.Due(
        [
            new DrillAttempt(item, DrillAnswer.Fold, false, now),
            new DrillAttempt(other, DrillAnswer.Raise, true, now),
        ]);
        var fixedSince = OpeningTrainingService.Due(
        [
            new DrillAttempt(item, DrillAnswer.Raise, true, now.AddMinutes(1)),
            new DrillAttempt(item, DrillAnswer.Fold, false, now),
        ]);

        Assert.Equal(new[] { item }, missed);
        Assert.Empty(fixedSince);
    }

    [Fact]
    public async Task Answers_are_checked_and_recorded()
    {
        var store = new MemoryStore();
        var service = new OpeningTrainingService(store, new LeakService(new NoStats()), new FakeTimeProvider(), new Random(1));
        var item = new DrillItem(TableFormat.SixMax, StackBand.Mid, PokerPosition.Utg, RangeNotation.Parse("72o").Single());

        var result = await service.AnswerAsync(Guid.NewGuid(), item, DrillAnswer.Raise, TestContext.Current.CancellationToken);

        Assert.False(result.Correct);
        Assert.Equal(DrillAnswer.Fold, result.Expected);
        Assert.Contains(RangeNotation.Parse("AA").Single(), result.ReferenceHands);
        Assert.False(Assert.Single(store.Attempts).Correct);
    }

    internal sealed class MemoryStore : ITrainingStore
    {
        public List<DrillAttempt> Attempts { get; } = [];

        public Task RecordAsync(Guid userId, DrillAttempt attempt, int referenceVersion, CancellationToken cancellationToken)
        {
            Attempts.Insert(0, attempt);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<Guid, bool>> RealHandOutcomesAsync(Guid userId, TableFormat format, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, bool>>(Attempts
                .Where(a => a.SourceHandId is not null)
                .GroupBy(a => a.SourceHandId!.Value)
                .ToDictionary(g => g.Key, g => g.First().Correct));

        public Task<(int Attempts, int Correct)> CountSinceAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult((Attempts.Count(a => a.AnsweredAt >= since), Attempts.Count(a => a.AnsweredAt >= since && a.Correct)));

        public Task<IReadOnlyList<DrillAttempt>> RecentAsync(Guid userId, TableFormat format, DrillMode mode, int count, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DrillAttempt>>(Attempts.Where(a => a.Item.IsDefence == (mode == DrillMode.Defence)).Take(count).ToList());
    }

    internal sealed class NoStats : IStatisticsReadStore
    {
        public Task<IReadOnlyList<(PokerPosition? Position, HeroStatCounts Counts)>> CountByPositionAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<(PokerPosition?, HeroStatCounts)>>([]);

        public Task<StatisticsSample> CountTournamentsAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult(new StatisticsSample(0, 0));

        public Task<FormatCounts> CountByFormatAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult(new FormatCounts(0, 0));

        public Task<IReadOnlyList<(DateOnly Month, HeroStatCounts Counts)>> CountByMonthAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>

            Task.FromResult<IReadOnlyList<(DateOnly, HeroStatCounts)>>([]);


        public Task<int> CountPendingAsync(Guid userId, int factsVersion, CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
