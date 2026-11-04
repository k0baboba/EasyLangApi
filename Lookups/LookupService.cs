using System.Text.Json;
using EasyLangApi.Auth;
using EasyLangApi.Data;
using EasyLangApi.Episodes;
using EasyLangApi.Explaining;
using EasyLangApi.Vocabulary;
using EasyLangApi.Quotas;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyLangApi.Lookups;

public sealed record LookupResult(
    int LookupId, int KnowledgeItemId, bool IsNewItem, string Phrase, PhraseContext Context, Explanation Explanation);

/// <summary>
/// Сценарий "разобрать фразу": собрать контекст вокруг реплики, спросить разборщик,
/// сохранить наблюдение (Lookup) и завести или найти элемент учёта (KnowledgeItem).
/// </summary>
public sealed class LookupService(
    AppDbContext db, IPhraseExplainer explainer, ICurrentUser currentUser, IOptions<QuotaOptions> quotas, TimeProvider time)
{
    public const int ContextRadius = 2;

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <returns>null, если реплики нет.</returns>
    public async Task<LookupResult?> ExplainAsync(int lineId, string phrase, CancellationToken cancellationToken)
    {
        var line = await db.Lines
            .Include(l => l.Episode)
            .FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
            return null;

        var now = time.GetUtcNow().UtcDateTime;
        await EnsureDailyQuotaAsync(now, cancellationToken);

        var context = await BuildContextAsync(line, cancellationToken);
        var explanation = await explainer.ExplainAsync(phrase, context, cancellationToken);

        var normalized = PhraseNormalizer.Normalize(phrase);
        var item = await db.KnowledgeItems.FirstOrDefaultAsync(k => k.PhraseNormalized == normalized, cancellationToken);
        var isNewItem = item is null;
        if (item is null)
        {
            // Новый элемент сразу доступен для повторения: так поток
            // "посмотрел → повторил" можно пройти за одну сессию.
            item = new KnowledgeItem
            {
                UserId = line.Episode.UserId,
                PhraseNormalized = normalized,
                FirstSeenAt = now,
                IntervalDays = 0,
                DueAt = now,
                Ease = 2.5,
            };
            db.KnowledgeItems.Add(item);
        }

        var lookup = new Lookup
        {
            Line = line,
            KnowledgeItem = item,
            Phrase = phrase,
            ExplanationJson = JsonSerializer.Serialize(explanation, JsonOptions),
            CreatedAt = now,
        };
        db.Lookups.Add(lookup);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (isNewItem && IsUniqueViolation(ex))
        {
            // Соседний запрос успел создать тот же элемент. Убираем свой из отслеживания,
            // берём его элемент и сохраняем наблюдение ещё раз.
            var existing = await db.KnowledgeItems.FirstOrDefaultAsync(k => k.PhraseNormalized == normalized, cancellationToken)
                           ?? throw new DuplicatePhraseException("Элемент словаря создаётся параллельно, повторите запрос.", ex);
            // Отсоединяем свой элемент. Наблюдение ссылалось на него обязательной связью, поэтому EF
            // отсоединит и его как «сироту». Добавляем наблюдение заново — уже к найденному элементу.
            db.Entry(item).State = EntityState.Detached;
            lookup.KnowledgeItem = existing;
            db.Lookups.Add(lookup);
            item = existing;
            isNewItem = false;
            await db.SaveChangesAsync(cancellationToken);
        }

        return new LookupResult(lookup.Id, item.Id, isNewItem, phrase, context, explanation);
    }

    /// <summary>SQLITE_CONSTRAINT_UNIQUE (2067): вставка нарушила уникальный индекс.</summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067 };

    /// <summary>Разборы стоят денег (LLM) — ограничиваем их число в сутки. Фильтр по пользователю добавит EF.</summary>
    private async Task EnsureDailyQuotaAsync(DateTime now, CancellationToken cancellationToken)
    {
        var limit = quotas.Value.LookupsPerDay;
        var dayStart = now.Date;
        var usedToday = await db.Lookups.CountAsync(l => l.CreatedAt >= dayStart, cancellationToken);
        if (usedToday >= limit)
            throw new QuotaExceededException($"Суточная квота разборов ({limit}) исчерпана для пользователя {currentUser.UserId}.");
    }

    /// <summary>Название эпизода, текущая реплика, две предыдущие и две следующие.</summary>
    private async Task<PhraseContext> BuildContextAsync(Line line, CancellationToken cancellationToken)
    {
        var neighbours = await db.Lines
            .Where(l => l.EpisodeId == line.EpisodeId
                        && l.Index >= line.Index - ContextRadius
                        && l.Index <= line.Index + ContextRadius
                        && l.Id != line.Id)
            .OrderBy(l => l.Index)
            .Select(l => new { l.Index, l.Text })
            .ToListAsync(cancellationToken);

        return new PhraseContext(
            Title: string.IsNullOrWhiteSpace(line.Episode.Title) ? null : line.Episode.Title,
            Speaker: SpeakerParser.TryExtract(line.Text),
            Before: neighbours.Where(n => n.Index < line.Index).Select(n => n.Text).ToList(),
            CurrentLine: line.Text,
            After: neighbours.Where(n => n.Index > line.Index).Select(n => n.Text).ToList());
    }
}
