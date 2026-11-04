using System.Text.Json;
using EasyLangApi.Data;
using EasyLangApi.Episodes;
using EasyLangApi.Explaining;
using EasyLangApi.Vocabulary;
using Microsoft.EntityFrameworkCore;

namespace EasyLangApi.Lookups;

public sealed record LookupResult(
    int LookupId, int KnowledgeItemId, bool IsNewItem, string Phrase, PhraseContext Context, Explanation Explanation);

/// <summary>
/// Сценарий "разобрать фразу": собрать контекст вокруг реплики, спросить разборщик,
/// сохранить наблюдение (Lookup) и завести или найти элемент учёта (KnowledgeItem).
/// </summary>
public sealed class LookupService(AppDbContext db, IPhraseExplainer explainer, TimeProvider time)
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

        var context = await BuildContextAsync(line, cancellationToken);
        var explanation = await explainer.ExplainAsync(phrase, context, cancellationToken);

        var now = time.GetUtcNow().UtcDateTime;
        var normalized = PhraseNormalizer.Normalize(phrase);
        var item = await db.KnowledgeItems.FirstOrDefaultAsync(k => k.PhraseNormalized == normalized, cancellationToken);
        var isNewItem = item is null;
        if (item is null)
        {
            // Новый элемент сразу доступен для повторения: так поток
            // "посмотрел → повторил" можно пройти за одну сессию.
            item = new KnowledgeItem
            {
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
        await db.SaveChangesAsync(cancellationToken);

        return new LookupResult(lookup.Id, item.Id, isNewItem, phrase, context, explanation);
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
            Title: line.Episode.Title,
            Speaker: SpeakerParser.TryExtract(line.Text),
            Before: neighbours.Where(n => n.Index < line.Index).Select(n => n.Text).ToList(),
            CurrentLine: line.Text,
            After: neighbours.Where(n => n.Index > line.Index).Select(n => n.Text).ToList());
    }
}
