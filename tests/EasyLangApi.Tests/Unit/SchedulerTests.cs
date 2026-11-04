using EasyLangApi.Data;
using EasyLangApi.Review;
using Microsoft.Extensions.Options;

namespace EasyLangApi.Tests.Unit;

public class SchedulerTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    private static KnowledgeItem Item(int id = 1, double interval = 0) =>
        new() { Id = id, PhraseNormalized = $"item{id}", FirstSeenAt = Start, DueAt = Start, IntervalDays = interval, Ease = 2.5 };

    /// <summary>История одного элемента: (через сколько дней от начала показали, вспомнил ли).</summary>
    private static List<ReviewEvent> History(int itemId, params (double Day, bool? Correct)[] answers) =>
        answers.Select((a, i) => new ReviewEvent
        {
            Id = itemId * 1000 + i,
            KnowledgeItemId = itemId,
            ShownAt = Start.AddDays(a.Day),
            AnsweredAt = a.Correct is null ? null : Start.AddDays(a.Day),
            WasCorrect = a.Correct,
        }).ToList();

    private static List<UserAnswer> Answers(KnowledgeItem item, IEnumerable<ReviewEvent> history) =>
        history.Where(e => e.WasCorrect is not null)
            .Select(e => new UserAnswer(e.Id, item.Id, item.FirstSeenAt, e.ShownAt, e.AnsweredAt!.Value, e.WasCorrect!.Value))
            .ToList();

    private static AdaptiveScheduler Adaptive(AdaptiveOptions? adaptive = null) =>
        new(Options.Create(new ReviewOptions { Adaptive = adaptive ?? new AdaptiveOptions() }));

    [Fact]
    public void Fixed_scheduler_follows_the_ladder()
    {
        var scheduler = new FixedIntervalScheduler(Options.Create(new ReviewOptions()));
        var history = History(1, (0, true));

        var schedule = scheduler.Schedule(Item(interval: 3), history, [], Start);

        Assert.Equal(7, schedule.IntervalDays);
        Assert.Equal(Start.AddDays(7), schedule.DueAt);
        Assert.Equal(2.5, schedule.Ease);
        Assert.Null(scheduler.ForgettingProbability(Item(), history, [], Start));
    }

    [Fact]
    public void Fixed_scheduler_uses_configured_steps()
    {
        var scheduler = new FixedIntervalScheduler(Options.Create(new ReviewOptions { FixedIntervals = [2, 5] }));

        Assert.Equal(5, scheduler.Schedule(Item(interval: 2), History(1, (0, true)), [], Start).IntervalDays);
        Assert.Equal(2, scheduler.Schedule(Item(interval: 5), History(1, (0, false)), [], Start).IntervalDays);
    }

    [Fact]
    public void Adaptive_cold_start_gives_minimal_interval()
    {
        // Веса стартуют с нулей, h = 2^0 = 1 день. Ответ сразу после знакомства (t = 0) градиента
        // не даёт, так что интервал 1 · log2(1/0.8) ≈ 0.32 дня округляется вниз и упирается в минимум.
        var item = Item();
        var history = History(1, (0, true));

        var schedule = Adaptive().Schedule(item, history, Answers(item, history), Start);

        Assert.Equal(1, schedule.IntervalDays);
        Assert.Equal(2.5, schedule.Ease); // адаптивный планировщик Ease не трогает
    }

    [Fact]
    public void Adaptive_learns_from_users_answers_on_other_items()
    {
        // Пользователь, который вспоминает слова даже после долгих перерывов, учит модель
        // растягивать интервалы — и это переносится на новый элемент с той же историей.
        var scheduler = Adaptive(new AdaptiveOptions { RoundToWholeDays = false, MinIntervalDays = 0.001 });
        var target = Item(id: 99);
        var targetHistory = History(99, (0, true));
        var targetAnswers = Answers(target, targetHistory);

        var strong = new List<UserAnswer>();
        var weak = new List<UserAnswer>();
        for (var id = 1; id <= 30; id++)
        {
            var item = Item(id);
            strong.AddRange(Answers(item, History(id, (3, true), (10, true), (30, true))));
            weak.AddRange(Answers(item, History(id, (1, false), (2, false), (3, false))));
        }

        var afterStrong = scheduler.Schedule(target, targetHistory, [.. strong, .. targetAnswers], Start).IntervalDays;
        var afterWeak = scheduler.Schedule(target, targetHistory, [.. weak, .. targetAnswers], Start).IntervalDays;

        Assert.True(afterStrong > afterWeak, $"{afterStrong} vs {afterWeak}");
    }

    // Следующие четыре теста повторяют AdaptiveSchedulerTests из srs/tests/test_schedulers.py.

    [Fact]
    public void Correct_answer_after_long_gap_increases_half_life()
    {
        // Модель ждала p = 1/8, а ученик вспомнил.
        var model = new HalfLifeRegression(learningRate: 0.05, l2: 1e-4);
        var before = model.HalfLifeDays(default);

        model.Observe(default, elapsedDays: 3, wasCorrect: true);

        Assert.True(model.HalfLifeDays(default) > before);
    }

    [Fact]
    public void Mistake_soon_after_learning_decreases_half_life()
    {
        var model = new HalfLifeRegression(learningRate: 0.05, l2: 1e-4, initialWeights: [3.0, 0, 0, 0]);
        var before = model.HalfLifeDays(default);

        model.Observe(default, elapsedDays: 1, wasCorrect: false);

        Assert.True(model.HalfLifeDays(default) < before);
    }

    [Fact]
    public void Learns_from_consistent_data()
    {
        // Ученик всегда вспоминает через 5 дней — модель должна поднять p(5 дней) выше 0.8.
        var model = new HalfLifeRegression(learningRate: 0.05, l2: 1e-4);
        for (var i = 0; i < 500; i++)
            model.Observe(default, elapsedDays: 5, wasCorrect: true);

        Assert.True(model.RecallProbability(default, 5) > 0.8);
    }

    [Fact]
    public void Interval_hits_target_and_is_clamped()
    {
        // h = 2^5 = 32 дня; p(t) = 0.9 при t = 32·log2(1/0.9) ≈ 4.86 → 5 дней.
        var hits = Adaptive(new AdaptiveOptions { TargetRetention = 0.9, InitialWeights = [5.0, 0, 0, 0] });
        var tiny = Adaptive(new AdaptiveOptions { InitialWeights = [-50, 0, 0, 0] });
        var huge = Adaptive(new AdaptiveOptions { InitialWeights = [50, 0, 0, 0], MaxIntervalDays = 200 });

        Assert.Equal(5, hits.Schedule(Item(), [], [], Start).IntervalDays);
        Assert.Equal(1, tiny.Schedule(Item(), [], [], Start).IntervalDays);
        Assert.Equal(200, huge.Schedule(Item(), [], [], Start).IntervalDays);
    }

    [Fact]
    public void Lower_target_means_longer_intervals()
    {
        double[] weights = [5.0, 0, 0, 0];
        var strict = Adaptive(new AdaptiveOptions { TargetRetention = 0.95, InitialWeights = weights });
        var lax = Adaptive(new AdaptiveOptions { TargetRetention = 0.7, InitialWeights = weights });

        Assert.True(strict.Schedule(Item(), [], [], Start).IntervalDays < lax.Schedule(Item(), [], [], Start).IntervalDays);
    }

    [Fact]
    public void Without_rounding_next_show_is_when_recall_drops_to_target()
    {
        var scheduler = Adaptive(new AdaptiveOptions { TargetRetention = 0.9, RoundToWholeDays = false, MinIntervalDays = 0.001 });
        var item = Item();
        var history = History(1, (1, true), (3, true));
        var answers = Answers(item, history);

        var schedule = scheduler.Schedule(item, history, answers, Start.AddDays(3));
        var forgetting = scheduler.ForgettingProbability(item, history, answers, schedule.DueAt)!.Value;

        Assert.Equal(0.1, forgetting, precision: 9);
    }

    [Fact]
    public void Forgetting_probability_grows_with_time_and_stays_in_range()
    {
        var scheduler = Adaptive();
        var item = Item();
        var history = History(1, (1, true));
        var answers = Answers(item, history);

        var values = new[] { 1, 2, 7, 30, 365 }
            .Select(day => scheduler.ForgettingProbability(item, history, answers, Start.AddDays(day))!.Value)
            .ToList();

        Assert.Equal(0, values[0], precision: 9);
        Assert.All(values.Zip(values.Skip(1)), pair => Assert.True(pair.Second > pair.First));
        Assert.All(values, v => Assert.InRange(v, 0, 1));
    }

    [Fact]
    public void Shows_without_answer_do_not_count()
    {
        var scheduler = Adaptive(new AdaptiveOptions { RoundToWholeDays = false });
        var item = Item();
        var withNoise = History(1, (1, true), (2, null), (3, null), (4, true));
        var clean = History(1, (1, true), (4, true));

        Assert.Equal(
            scheduler.Schedule(item, clean, Answers(item, clean), Start.AddDays(4)).IntervalDays,
            scheduler.Schedule(item, withNoise, Answers(item, withNoise), Start.AddDays(4)).IntervalDays);
    }

    [Fact]
    public void Estimator_trained_once_gives_the_same_numbers_as_single_calls()
    {
        var scheduler = Adaptive();
        var a = Item(1);
        var b = Item(2);
        var historyA = History(1, (2, true), (5, false));
        var historyB = History(2, (3, true));
        var answers = Answers(a, historyA).Concat(Answers(b, historyB)).ToList();
        var at = Start.AddDays(9);

        var estimator = scheduler.CreateEstimator(answers);

        Assert.Equal(scheduler.ForgettingProbability(a, historyA, answers, at), estimator.ForgettingProbability(a, historyA, at));
        Assert.Equal(scheduler.ForgettingProbability(b, historyB, answers, at), estimator.ForgettingProbability(b, historyB, at));
        Assert.Null(new FixedIntervalScheduler(Options.Create(new ReviewOptions())).CreateEstimator(answers));
    }

    [Theory]
    [InlineData(SchedulerKind.Adaptive, null, QueueOrder.ForgettingProbability)]
    [InlineData(SchedulerKind.Fixed, null, QueueOrder.DueDate)]
    [InlineData(SchedulerKind.Adaptive, QueueOrder.DueDate, QueueOrder.DueDate)]
    [InlineData(SchedulerKind.Fixed, QueueOrder.ForgettingProbability, QueueOrder.ForgettingProbability)]
    public void Queue_order_defaults_to_forgetting_only_for_adaptive(SchedulerKind kind, QueueOrder? configured, QueueOrder expected) =>
        Assert.Equal(expected, new ReviewOptions { Scheduler = kind, QueueOrder = configured }.EffectiveQueueOrder);

    [Fact]
    public void Answers_are_learned_in_the_order_they_were_given()
    {
        var scheduler = Adaptive();
        var a = Item(1);
        var b = Item(2);
        var answers = Answers(a, History(1, (2, true), (5, false))).Concat(Answers(b, History(2, (3, true)))).ToList();
        var shuffled = answers.AsEnumerable().Reverse().ToList();

        Assert.Equal(scheduler.Train(answers).Weights, scheduler.Train(shuffled).Weights);
    }
}
