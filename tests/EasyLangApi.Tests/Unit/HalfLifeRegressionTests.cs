using System.Text.Json;
using EasyLangApi.Data;
using EasyLangApi.Review;
using Microsoft.Extensions.Options;

namespace EasyLangApi.Tests.Unit;

/// <summary>
/// Перекрёстная проверка порта модели: те же входы, что у Python-версии из srs/,
/// должны давать те же веса, вероятности и интервалы — бит в бит, без допуска.
/// (Эталоны получены на Python 3.13 с glibc; Math.Pow и Math.Log2 в .NET на Linux — те же функции libm.)
/// </summary>
public class HalfLifeRegressionTests
{

    [Theory]
    // Ожидаемое — sum(w * xi for w, xi in zip(...)) в Python 3.13; наивная сумма дала бы
    // 0.6000000000000001 и 1.0 соответственно.
    [InlineData(new[] { 0.1, 0.2, 0.3, 0.0 }, 0.6)]
    [InlineData(new[] { 1e16, 1.0, -1e16, 1.0 }, 2.0)]
    public void Dot_product_matches_python_compensated_sum(double[] weights, double expected)
    {
        Assert.Equal(expected, HalfLifeRegression.DotProduct(weights, [1.0, 1.0, 1.0, 1.0]));
    }

    [Fact]
    public void Model_matches_python_on_explicit_sequence()
    {
        // Последовательность и ожидаемые числа получены из srs_sim.schedulers.AdaptiveScheduler
        // (target_recall=0.8, остальное по умолчанию) на Python 3.13.
        (int Item, double Elapsed, bool Correct)[] sequence =
        [
            (1, 1, true), (2, 1, false), (1, 3, true), (3, 2, true), (2, 1, true), (1, 7, false), (3, 5, true),
            (2, 4, true), (1, 1, true), (3, 12, false), (2, 9, true), (1, 2, true), (4, 30, true), (4, 60, false),
            (1, 15, true),
        ];
        var model = new HalfLifeRegression(learningRate: 0.05, l2: 1e-4);
        var stats = new Dictionary<int, ItemStats>();
        foreach (var (item, elapsed, correct) in sequence)
        {
            var before = stats.GetValueOrDefault(item);
            model.Observe(before, elapsed, correct);
            stats[item] = before.After(correct);
        }

        double[] expectedWeights = [0.09573691513848234, 0.08408860700697711, 0.054034571139350444, 0.022479410051635174];
        for (var i = 0; i < expectedWeights.Length; i++)
            Assert.Equal(expectedWeights[i], model.Weights[i]);
        Assert.Equal(1.2638272935954276, model.HalfLifeDays(stats[1]));
        Assert.Equal(1.2272398424634008, model.HalfLifeDays(stats[2]));
        Assert.Equal(1.2236323421779682, model.HalfLifeDays(stats[3]));
        Assert.Equal(1.194444237526481, model.HalfLifeDays(stats[4]));
        Assert.Equal(1.0686111052291036, model.HalfLifeDays(default));
    }

    [Fact]
    public void Adaptive_scheduler_reproduces_python_decisions_step_by_step()
    {
        // Assets/hlr-golden.json — журнал Python-планировщика на детерминированном сценарии
        // (srs/srs_sim/golden.py). Идём по журналу и на каждом шаге спрашиваем решение у C#.
        var golden = JsonSerializer.Deserialize<Golden>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "hlr-golden.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var scheduler = new AdaptiveScheduler(Options.Create(new ReviewOptions
        {
            Adaptive = new AdaptiveOptions { TargetRetention = golden.Target },
        }));

        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var items = Enumerable.Range(0, golden.Items).ToDictionary(i => i, i => new KnowledgeItem
        {
            Id = i + 1,
            FirstSeenAt = start.AddDays(i < 10 ? 0 : 3),
        });
        var histories = items.Keys.ToDictionary(i => i, _ => new List<ReviewEvent>());
        var due = items.ToDictionary(p => p.Key, p => p.Value.FirstSeenAt.AddDays(1)); // next_interval([]) = 1
        var answers = new List<UserAnswer>();

        for (var step = 0; step < golden.Reviews.Count; step++)
        {
            var review = golden.Reviews[step];
            var item = items[review.Item];
            var history = histories[review.Item];
            var at = start.AddDays(review.Day);
            // Python показал элемент именно тогда, когда его назначил C#: расписания совпадают.
            Assert.Equal(due[review.Item], at);

            var forgetting = scheduler.ForgettingProbability(item, history, answers, at)!.Value;
            Assert.True(review.Forgetting == forgetting, $"шаг {step}: p забыть {forgetting:R} вместо {review.Forgetting:R}");

            var shown = new ReviewEvent
            {
                Id = step + 1, KnowledgeItemId = item.Id, ShownAt = at, AnsweredAt = at, WasCorrect = review.Correct,
            };
            history.Add(shown);
            answers.Add(new UserAnswer(shown.Id, item.Id, item.FirstSeenAt, at, at, review.Correct));

            var schedule = scheduler.Schedule(item, history, answers, at);
            Assert.True(review.Interval == schedule.IntervalDays, $"шаг {step}: интервал {schedule.IntervalDays} вместо {review.Interval}");
            due[review.Item] = schedule.DueAt;
        }

        var weights = scheduler.Train(answers).Weights;
        for (var i = 0; i < golden.Weights.Count; i++)
            Assert.Equal(golden.Weights[i], weights[i]);
    }

    private sealed record Golden(double Target, int Days, int Items, List<double> Weights, List<GoldenReview> Reviews);

    private sealed record GoldenReview(int Day, int Item, bool Correct, double Forgetting, int Interval);
}
