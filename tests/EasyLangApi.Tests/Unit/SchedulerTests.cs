using EasyLangApi.Data;
using EasyLangApi.Review;
using Microsoft.Extensions.Options;

namespace EasyLangApi.Tests.Unit;

public class SchedulerTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    private static KnowledgeItem Item(double interval = 0) =>
        new() { Id = 1, PhraseNormalized = "beat", FirstSeenAt = Start, DueAt = Start, IntervalDays = interval, Ease = 2.5 };

    /// <summary>История: (через сколько дней от начала показали, вспомнил ли).</summary>
    private static List<ReviewEvent> History(params (double Day, bool? Correct)[] answers) =>
        answers.Select((a, i) => new ReviewEvent
        {
            Id = i + 1,
            KnowledgeItemId = 1,
            ShownAt = Start.AddDays(a.Day),
            AnsweredAt = a.Correct is null ? null : Start.AddDays(a.Day),
            WasCorrect = a.Correct,
        }).ToList();

    private static DateTime LastDay(List<ReviewEvent> history) => history[^1].ShownAt;

    private static AdaptiveScheduler Adaptive(AdaptiveOptions? adaptive = null) =>
        new(Options.Create(new ReviewOptions { Adaptive = adaptive ?? new AdaptiveOptions() }));

    [Fact]
    public void Fixed_scheduler_follows_the_ladder()
    {
        var scheduler = new FixedIntervalScheduler(Options.Create(new ReviewOptions()));
        var history = History((0, true));

        var schedule = scheduler.Schedule(Item(interval: 3), history, LastDay(history));

        Assert.Equal(7, schedule.IntervalDays);
        Assert.Equal(LastDay(history).AddDays(7), schedule.DueAt);
        Assert.Equal(2.5, schedule.Ease);
        Assert.Null(scheduler.ForgettingProbability(Item(), history, Start));
    }

    [Fact]
    public void Fixed_scheduler_uses_configured_steps()
    {
        var scheduler = new FixedIntervalScheduler(Options.Create(new ReviewOptions { FixedIntervals = [2, 5] }));

        Assert.Equal(5, scheduler.Schedule(Item(interval: 2), History((0, true)), Start).IntervalDays);
        Assert.Equal(2, scheduler.Schedule(Item(interval: 5), History((0, false)), Start).IntervalDays);
    }

    [Fact]
    public void Adaptive_intervals_grow_with_each_success()
    {
        var scheduler = Adaptive();
        var intervals = new List<double>();
        var answers = new List<(double, bool?)>();
        var day = 0.0;
        for (var i = 0; i < 5; i++)
        {
            answers.Add((day, true));
            var history = History(answers.ToArray());
            var schedule = scheduler.Schedule(Item(), history, LastDay(history));
            intervals.Add(schedule.IntervalDays);
            day += schedule.IntervalDays;
        }

        Assert.All(intervals.Zip(intervals.Skip(1)), pair => Assert.True(pair.Second > pair.First, $"{pair.First} → {pair.Second}"));
    }

    [Fact]
    public void Adaptive_failure_shortens_the_interval()
    {
        var scheduler = Adaptive();
        var remembered = History((0, true), (4, true));
        var forgotten = History((0, true), (4, false));

        var afterSuccess = scheduler.Schedule(Item(), remembered, LastDay(remembered)).IntervalDays;
        var afterFailure = scheduler.Schedule(Item(), forgotten, LastDay(forgotten)).IntervalDays;

        Assert.True(afterFailure < afterSuccess / 2, $"{afterFailure} vs {afterSuccess}");
    }

    [Fact]
    public void Item_remembered_after_long_gaps_is_treated_as_easier()
    {
        var scheduler = Adaptive();
        var longGaps = History((0, true), (10, true), (40, true));
        var shortGaps = History((0, true), (1, true), (2, true));

        var easy = scheduler.Schedule(Item(), longGaps, LastDay(longGaps));
        var usual = scheduler.Schedule(Item(), shortGaps, LastDay(shortGaps));

        Assert.True(easy.Ease > 1, $"ease {easy.Ease}");
        Assert.True(easy.IntervalDays > usual.IntervalDays, $"{easy.IntervalDays} vs {usual.IntervalDays}");
    }

    [Fact]
    public void Item_forgotten_quickly_is_treated_as_harder()
    {
        var scheduler = Adaptive();
        var history = History((0, true), (0.5, false), (1, false), (1.5, true), (2, false));

        var schedule = scheduler.Schedule(Item(), history, LastDay(history));

        Assert.True(schedule.Ease < 1, $"ease {schedule.Ease}");
    }

    [Fact]
    public void Next_show_is_when_recall_drops_to_target_retention()
    {
        var scheduler = Adaptive(new AdaptiveOptions { TargetRetention = 0.9 });
        var history = History((0, true), (3, true));

        var schedule = scheduler.Schedule(Item(), history, LastDay(history));
        var forgetting = scheduler.ForgettingProbability(Item(), history, schedule.DueAt)!.Value;

        Assert.Equal(0.1, forgetting, precision: 3);
    }

    [Fact]
    public void Forgetting_probability_grows_with_time_and_stays_in_range()
    {
        var scheduler = Adaptive();
        var history = History((0, true));

        var values = new[] { 0, 1, 7, 30, 365 }
            .Select(days => scheduler.ForgettingProbability(Item(), history, Start.AddDays(days))!.Value)
            .ToList();

        Assert.Equal(0, values[0], precision: 6);
        Assert.All(values.Zip(values.Skip(1)), pair => Assert.True(pair.Second > pair.First));
        Assert.All(values, v => Assert.InRange(v, 0, 1));
    }

    [Fact]
    public void Shows_without_answer_do_not_count()
    {
        var scheduler = Adaptive();
        var withNoise = History((0, true), (1, null), (2, null), (3, true));
        var clean = History((0, true), (3, true));

        Assert.Equal(
            scheduler.Schedule(Item(), clean, LastDay(clean)).IntervalDays,
            scheduler.Schedule(Item(), withNoise, LastDay(withNoise)).IntervalDays);
    }
}
