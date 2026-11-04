using EasyLangApi.Data;

namespace EasyLangApi.Review;

public sealed record ReviewSchedule(double IntervalDays, DateTime DueAt, double Ease);

/// <summary>Решает, когда показать элемент снова. Реализация выбирается настройкой Review:Scheduler.</summary>
public interface IReviewScheduler
{
    /// <param name="item">Элемент до пересчёта.</param>
    /// <param name="history">Все показы элемента по времени, последний — только что отвеченный.</param>
    ReviewSchedule Schedule(KnowledgeItem item, IReadOnlyList<ReviewEvent> history, DateTime now);

    /// <summary>Вероятность, что к моменту <paramref name="at"/> элемент забыт; null — модель этого не умеет.</summary>
    double? ForgettingProbability(KnowledgeItem item, IReadOnlyList<ReviewEvent> history, DateTime at);
}
