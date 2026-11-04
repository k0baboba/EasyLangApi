using System.Text.Json;
using EasyLangApi.Data;
using EasyLangApi.Explaining;
using EasyLangApi.Lookups;
using Microsoft.EntityFrameworkCore;

namespace EasyLangApi.Review;

public sealed record ReviewCard(
    int ReviewId,
    int KnowledgeItemId,
    string Phrase,
    DateTime DueAt,
    double IntervalDays,
    int LineId,
    string LineText,
    string EpisodeTitle,
    bool SingleUnknown,
    IReadOnlyList<string> OtherUnknown,
    double? ForgettingProbability,
    Explanation? Explanation);

public sealed record ReviewAnswer(int ReviewId, int KnowledgeItemId, bool WasCorrect, double IntervalDays, DateTime DueAt);

public enum AnswerStatus
{
    Accepted,
    NotFound,
    AlreadyAnswered,
}

public sealed record AnswerOutcome(AnswerStatus Status, ReviewAnswer? Answer = null);

/// <summary>
/// Показ на повторение и приём ответа. Каждый показ — новая строка ReviewEvent,
/// ответ дописывается в неё же. Так в истории видно и показы без ответа.
/// </summary>
public sealed class ReviewService(
    AppDbContext db, IReviewScheduler scheduler, ExampleLineFinder examples, TimeProvider time)
{
    /// <returns>null, если повторять сейчас нечего.</returns>
    public async Task<ReviewCard?> NextAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var item = await db.KnowledgeItems
            .Where(k => k.DueAt <= now)
            .OrderBy(k => k.DueAt)
            .ThenBy(k => k.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (item is null)
            return null;

        var lookup = await db.Lookups
            .Where(l => l.KnowledgeItemId == item.Id)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new { l.LineId, LineText = l.Line.Text, EpisodeTitle = l.Line.Episode.Title, l.ExplanationJson })
            .FirstAsync(cancellationToken);

        // Показываем реплику, где незнакомо только повторяемое. Лучше та, где фразу смотрели;
        // если в ней есть другие невыученные элементы — другая подходящая; если таких нет — всё же она.
        var analyzed = await examples.FindAsync(item, cancellationToken);
        var line = analyzed
                       .Where(l => l.IsSingleUnknown)
                       .OrderByDescending(l => l.LineId == lookup.LineId)
                       .ThenByDescending(l => l.LineId)
                       .FirstOrDefault()
                   ?? analyzed.FirstOrDefault(l => l.LineId == lookup.LineId)
                   ?? new SelectedLine(lookup.LineId, lookup.LineText, lookup.EpisodeTitle, []);

        var history = await LoadHistoryAsync(item.Id, cancellationToken);
        var forgetting = scheduler.ForgettingProbability(item, history, now);

        var shown = new ReviewEvent { KnowledgeItemId = item.Id, ShownAt = now };
        db.ReviewEvents.Add(shown);
        await db.SaveChangesAsync(cancellationToken);

        return new ReviewCard(
            shown.Id,
            item.Id,
            item.PhraseNormalized,
            item.DueAt,
            item.IntervalDays,
            line.LineId,
            line.Text,
            line.EpisodeTitle,
            line.IsSingleUnknown,
            line.OtherUnknown,
            forgetting is null ? null : Math.Round(forgetting.Value, 3),
            JsonSerializer.Deserialize<Explanation>(lookup.ExplanationJson, LookupService.JsonOptions));
    }

    public async Task<AnswerOutcome> AnswerAsync(int reviewId, bool wasCorrect, CancellationToken cancellationToken)
    {
        var shown = await db.ReviewEvents
            .Include(e => e.KnowledgeItem)
            .FirstOrDefaultAsync(e => e.Id == reviewId, cancellationToken);
        if (shown is null)
            return new AnswerOutcome(AnswerStatus.NotFound);
        if (shown.AnsweredAt is not null)
            return new AnswerOutcome(AnswerStatus.AlreadyAnswered);

        var now = time.GetUtcNow().UtcDateTime;
        var history = await LoadHistoryAsync(shown.KnowledgeItemId, cancellationToken);
        // shown уже отслеживается контекстом, поэтому в history это тот же объект.
        shown.AnsweredAt = now;
        shown.WasCorrect = wasCorrect;

        var item = shown.KnowledgeItem;
        var schedule = scheduler.Schedule(item, history, now);
        item.IntervalDays = schedule.IntervalDays;
        item.DueAt = schedule.DueAt;
        item.Ease = schedule.Ease;

        await db.SaveChangesAsync(cancellationToken);
        return new AnswerOutcome(
            AnswerStatus.Accepted,
            new ReviewAnswer(shown.Id, item.Id, wasCorrect, item.IntervalDays, item.DueAt));
    }

    private Task<List<ReviewEvent>> LoadHistoryAsync(int knowledgeItemId, CancellationToken cancellationToken) =>
        db.ReviewEvents
            .Where(e => e.KnowledgeItemId == knowledgeItemId)
            .OrderBy(e => e.ShownAt)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);
}
