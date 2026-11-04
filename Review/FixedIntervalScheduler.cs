using EasyLangApi.Data;
using Microsoft.Extensions.Options;

namespace EasyLangApi.Review;

/// <summary>Лесенка из V1: помнил — следующая ступенька, не помнил — с первой.</summary>
public sealed class FixedIntervalScheduler(IOptions<ReviewOptions> options) : IReviewScheduler
{
    public ReviewSchedule Schedule(KnowledgeItem item, IReadOnlyList<ReviewEvent> history, DateTime now)
    {
        var last = history.LastOrDefault(e => e.WasCorrect is not null)
                   ?? throw new ArgumentException("В истории нет ответа, пересчитывать нечего.", nameof(history));

        var steps = options.Value.FixedIntervals is { Length: > 0 } configured ? configured : IntervalLadder.DefaultSteps;
        var interval = IntervalLadder.Next(item.IntervalDays, last.WasCorrect!.Value, steps);
        return new ReviewSchedule(interval, now.AddDays(interval), item.Ease);
    }

    public double? ForgettingProbability(KnowledgeItem item, IReadOnlyList<ReviewEvent> history, DateTime at) => null;
}
