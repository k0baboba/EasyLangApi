using EasyLangApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyLangApi.Review;

/// <summary>Ищет в сохранённых репликах примеры с элементом и отмечает, что в них ещё незнакомо.</summary>
public sealed class ExampleLineFinder(AppDbContext db, IOptions<ReviewOptions> options)
{
    public const int MaxCandidates = 200;

    public async Task<IReadOnlyList<SelectedLine>> FindAsync(KnowledgeItem item, CancellationToken cancellationToken)
    {
        // Грубый отбор в SQL по самому длинному слову фразы, точная проверка по словам — в памяти.
        // Апострофы в тексте бывают прямые и типографские, поэтому ищем по куску до апострофа.
        var probe = item.PhraseNormalized
            .Split(' ', '\'')
            .OrderByDescending(part => part.Length)
            .First();

        var candidates = await db.Lines
            .Where(l => EF.Functions.Like(l.Text, "%" + probe + "%"))
            .OrderByDescending(l => l.Id)
            .Take(MaxCandidates)
            .Select(l => new CandidateLine(l.Id, l.Text, l.Episode.Title))
            .ToListAsync(cancellationToken);

        var knownAfter = options.Value.KnownAfterDays;
        var otherUnknown = await db.KnowledgeItems
            .Where(k => k.Id != item.Id && k.IntervalDays < knownAfter)
            .Select(k => k.PhraseNormalized)
            .ToListAsync(cancellationToken);

        return SingleUnknownSelector.Analyze(item.PhraseNormalized, candidates, otherUnknown);
    }
}
