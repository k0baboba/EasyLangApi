using EasyLangApi.Data;
using Microsoft.Extensions.Options;

namespace EasyLangApi.Review;

/// <summary>
/// Адаптивный планировщик на обучаемой модели <see cref="HalfLifeRegression"/> (перенос из srs/).
///
/// Веса модели нигде не хранятся: при каждом решении они заново обучаются прогоном по всем
/// ответам пользователя в том порядке, в каком ответы были даны. Это тот же онлайн-градиентный
/// спуск, что в srs/, только его состояние — функция таблицы ReviewEvent, а не отдельная таблица.
/// Поэтому веса у каждого пользователя свои, переживают перезапуск и не расходятся с историей.
/// Цена — O(число ответов) на решение; для одного человека это тысячи шагов, доли миллисекунды.
///
/// Следующий показ — когда предсказанная вероятность вспомнить опустится до TargetRetention:
/// t = h · log2(1 / TargetRetention), с округлением до дней и ограничением [Min; Max], как в srs/.
/// Ease этот планировщик не меняет: у модели нет отдельной сложности элемента.
/// </summary>
public sealed class AdaptiveScheduler(IOptions<ReviewOptions> options) : IReviewScheduler
{
    private AdaptiveOptions Settings => options.Value.Adaptive;

    public ReviewSchedule Schedule(
        KnowledgeItem item, IReadOnlyList<ReviewEvent> itemHistory, IReadOnlyList<UserAnswer> userAnswers, DateTime now)
    {
        var model = Train(userAnswers);
        var interval = IntervalDays(model.HalfLifeDays(StatsOf(itemHistory)));
        return new ReviewSchedule(interval, now.AddDays(interval), item.Ease);
    }

    public double? ForgettingProbability(
        KnowledgeItem item, IReadOnlyList<ReviewEvent> itemHistory, IReadOnlyList<UserAnswer> userAnswers, DateTime at)
    {
        var model = Train(userAnswers);
        var elapsedDays = Math.Max(0, (at - LastContact(item, itemHistory)).TotalDays);
        return 1 - model.RecallProbability(StatsOf(itemHistory), elapsedDays);
    }

    /// <summary>Онлайн-обучение по ответам пользователя в порядке их поступления.</summary>
    public HalfLifeRegression Train(IReadOnlyList<UserAnswer> userAnswers)
    {
        var settings = Settings;
        var model = new HalfLifeRegression(settings.LearningRate, settings.L2, settings.InitialWeights);
        var stats = new Dictionary<int, ItemStats>();
        var lastContact = new Dictionary<int, DateTime>();

        foreach (var answer in userAnswers.OrderBy(a => a.AnsweredAt).ThenBy(a => a.ReviewEventId))
        {
            var before = stats.GetValueOrDefault(answer.KnowledgeItemId);
            var previous = lastContact.TryGetValue(answer.KnowledgeItemId, out var seen) ? seen : answer.ItemFirstSeenAt;
            var elapsedDays = Math.Max(0, (answer.ShownAt - previous).TotalDays);

            model.Observe(before, elapsedDays, answer.WasCorrect);

            stats[answer.KnowledgeItemId] = before.After(answer.WasCorrect);
            lastContact[answer.KnowledgeItemId] = answer.AnsweredAt;
        }

        return model;
    }

    internal double IntervalDays(double halfLifeDays)
    {
        var settings = Settings;
        var interval = halfLifeDays * Math.Log2(1.0 / settings.TargetRetention);
        if (settings.RoundToWholeDays)
            interval = Math.Round(interval); // как round() в Python: половины к чётному
        return Math.Max(settings.MinIntervalDays, Math.Min(settings.MaxIntervalDays, interval));
    }

    private static ItemStats StatsOf(IReadOnlyList<ReviewEvent> itemHistory) =>
        ItemStats.From(Answered(itemHistory).Select(e => e.WasCorrect!.Value));

    private static DateTime LastContact(KnowledgeItem item, IReadOnlyList<ReviewEvent> itemHistory) =>
        Answered(itemHistory).Select(e => e.AnsweredAt ?? e.ShownAt).DefaultIfEmpty(item.FirstSeenAt).Max();

    private static IEnumerable<ReviewEvent> Answered(IReadOnlyList<ReviewEvent> history) =>
        history.Where(e => e.WasCorrect is not null).OrderBy(e => e.AnsweredAt ?? e.ShownAt).ThenBy(e => e.Id);
}
