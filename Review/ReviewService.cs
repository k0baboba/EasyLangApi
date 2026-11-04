using System.Text.Json;
using EasyLangApi.Data;
using EasyLangApi.Explaining;
using EasyLangApi.Lookups;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
    AppDbContext db, IReviewScheduler scheduler, ExampleLineFinder examples, IOptions<ReviewOptions> options, TimeProvider time)
{
    /// <returns>null, если повторять сейчас нечего.</returns>
    public async Task<ReviewCard?> NextAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var userAnswers = await LoadUserAnswersAsync(cancellationToken);
        var estimator = options.Value.EffectiveQueueOrder == QueueOrder.ForgettingProbability
            ? scheduler.CreateEstimator(userAnswers)
            : null;

        var item = estimator is null
            ? await MostOverdueAsync(now, cancellationToken)
            : await MostAtRiskAsync(estimator, now, cancellationToken);
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
        var forgetting = estimator is not null
            ? estimator.ForgettingProbability(item, history, now)
            : scheduler.ForgettingProbability(item, history, userAnswers, now);

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
        // Ответы из базы — без текущего: он ещё не сохранён. Добавляем его сами.
        var userAnswers = await LoadUserAnswersAsync(cancellationToken);

        // Проверка «ещё не отвечено» и запись ответа — одна операция в базе:
        // UPDATE ... WHERE Id = @id AND AnsweredAt IS NULL. Параллельный ответ обновит 0 строк.
        var claimed = await db.ReviewEvents
            .Where(e => e.Id == reviewId && e.AnsweredAt == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(e => e.AnsweredAt, now)
                .SetProperty(e => e.WasCorrect, wasCorrect), cancellationToken);
        if (claimed == 0)
            return new AnswerOutcome(AnswerStatus.AlreadyAnswered);

        // ExecuteUpdate прошёл мимо отслеживания. shown — тот же объект, что в history, поэтому
        // обновляем его в памяти. Пишем и исходное значение: так EF считает поле уже сохранённым
        // (IsModified = false здесь не подходит — он вернул бы старое значение).
        var entry = db.Entry(shown);
        entry.Property(e => e.AnsweredAt).OriginalValue = now;
        entry.Property(e => e.AnsweredAt).CurrentValue = now;
        entry.Property(e => e.WasCorrect).OriginalValue = wasCorrect;
        entry.Property(e => e.WasCorrect).CurrentValue = wasCorrect;
        var item = shown.KnowledgeItem;
        userAnswers.Add(new UserAnswer(shown.Id, item.Id, item.FirstSeenAt, shown.ShownAt, now, wasCorrect));

        var schedule = scheduler.Schedule(item, history, userAnswers, now);
        item.IntervalDays = schedule.IntervalDays;
        item.DueAt = schedule.DueAt;
        item.Ease = schedule.Ease;

        await db.SaveChangesAsync(cancellationToken);
        return new AnswerOutcome(
            AnswerStatus.Accepted,
            new ReviewAnswer(shown.Id, item.Id, wasCorrect, item.IntervalDays, item.DueAt));
    }

    /// <summary>Очередь по дате: самый просроченный элемент, при равенстве — меньший id.</summary>
    private Task<KnowledgeItem?> MostOverdueAsync(DateTime now, CancellationToken cancellationToken) =>
        db.KnowledgeItems
            .Where(k => k.DueAt <= now)
            .OrderBy(k => k.DueAt)
            .ThenBy(k => k.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Очередь по риску: среди элементов со сроком — тот, что вероятнее всего забыт сейчас; при равенстве —
    /// как в очереди по дате. В отличие от симуляции srs/ (порядок на день считается один раз утром),
    /// здесь выбор пересчитывается на каждый запрос с моделью, обученной на всех ответах к этому моменту.
    /// </summary>
    private async Task<KnowledgeItem?> MostAtRiskAsync(
        IForgettingEstimator estimator, DateTime now, CancellationToken cancellationToken)
    {
        var due = await db.KnowledgeItems.Where(k => k.DueAt <= now).ToListAsync(cancellationToken);
        if (due.Count == 0)
            return null;

        var ids = due.Select(k => k.Id).ToList();
        var histories = (await db.ReviewEvents
                .Where(e => ids.Contains(e.KnowledgeItemId))
                .OrderBy(e => e.ShownAt)
                .ThenBy(e => e.Id)
                .ToListAsync(cancellationToken))
            .ToLookup(e => e.KnowledgeItemId);

        return due
            .Select(k => (Item: k, Risk: estimator.ForgettingProbability(k, histories[k.Id].ToList(), now)))
            .OrderByDescending(x => x.Risk)
            .ThenBy(x => x.Item.DueAt)
            .ThenBy(x => x.Item.Id)
            .First()
            .Item;
    }

    /// <summary>Все ответы текущего пользователя (фильтр по владельцу добавит EF) — на них учится адаптивная модель.</summary>
    private Task<List<UserAnswer>> LoadUserAnswersAsync(CancellationToken cancellationToken) =>
        db.ReviewEvents
            .Where(e => e.WasCorrect != null && e.AnsweredAt != null)
            .Select(e => new UserAnswer(
                e.Id, e.KnowledgeItemId, e.KnowledgeItem.FirstSeenAt, e.ShownAt, e.AnsweredAt!.Value, e.WasCorrect!.Value))
            .ToListAsync(cancellationToken);

    private Task<List<ReviewEvent>> LoadHistoryAsync(int knowledgeItemId, CancellationToken cancellationToken) =>
        db.ReviewEvents
            .Where(e => e.KnowledgeItemId == knowledgeItemId)
            .OrderBy(e => e.ShownAt)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);
}
