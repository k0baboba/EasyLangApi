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
    string LineText,
    string EpisodeTitle,
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
public sealed class ReviewService(AppDbContext db, TimeProvider time)
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

        // Показываем фразу в той реплике, где её смотрели последний раз.
        var lookup = await db.Lookups
            .Where(l => l.KnowledgeItemId == item.Id)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new { LineText = l.Line.Text, EpisodeTitle = l.Line.Episode.Title, l.ExplanationJson })
            .FirstAsync(cancellationToken);

        var shown = new ReviewEvent { KnowledgeItemId = item.Id, ShownAt = now };
        db.ReviewEvents.Add(shown);
        await db.SaveChangesAsync(cancellationToken);

        return new ReviewCard(
            shown.Id,
            item.Id,
            item.PhraseNormalized,
            item.DueAt,
            item.IntervalDays,
            lookup.LineText,
            lookup.EpisodeTitle,
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
        shown.AnsweredAt = now;
        shown.WasCorrect = wasCorrect;

        var item = shown.KnowledgeItem;
        item.IntervalDays = IntervalLadder.Next(item.IntervalDays, wasCorrect);
        item.DueAt = now.AddDays(item.IntervalDays);

        await db.SaveChangesAsync(cancellationToken);
        return new AnswerOutcome(
            AnswerStatus.Accepted,
            new ReviewAnswer(shown.Id, item.Id, wasCorrect, item.IntervalDays, item.DueAt));
    }
}
