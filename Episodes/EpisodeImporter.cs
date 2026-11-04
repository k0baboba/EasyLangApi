using EasyLangApi.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyLangApi.Episodes;

/// <summary>
/// Сохраняет реплики из любого ITextSource: создаёт эпизод или дописывает в конец
/// существующего (так скриншоты одной игровой сессии становятся одним эпизодом
/// и дают друг другу контекст).
/// </summary>
public sealed class EpisodeImporter(AppDbContext db, TimeProvider time)
{
    public async Task<Episode> ImportAsync(
        string title, string sourceFile, IReadOnlyList<SourceLine> lines, CancellationToken cancellationToken)
    {
        var episode = new Episode
        {
            Title = title,
            SourceFile = sourceFile,
            CreatedAt = time.GetUtcNow().UtcDateTime,
            Lines = ToEntities(lines, firstIndex: 0),
        };

        db.Episodes.Add(episode);
        await db.SaveChangesAsync(cancellationToken);
        return episode;
    }

    /// <returns>Добавленные реплики или null, если эпизода нет.</returns>
    public async Task<List<Line>?> AppendAsync(int episodeId, IReadOnlyList<SourceLine> lines, CancellationToken cancellationToken)
    {
        if (!await db.Episodes.AnyAsync(e => e.Id == episodeId, cancellationToken))
            return null;

        var lastIndex = await db.Lines
            .Where(l => l.EpisodeId == episodeId)
            .MaxAsync(l => (int?)l.Index, cancellationToken) ?? -1;

        var added = ToEntities(lines, firstIndex: lastIndex + 1);
        foreach (var line in added)
            line.EpisodeId = episodeId;

        db.Lines.AddRange(added);
        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    private static List<Line> ToEntities(IReadOnlyList<SourceLine> lines, int firstIndex) =>
        lines.Select((line, offset) => new Line
        {
            Index = firstIndex + offset,
            StartMs = line.StartMs,
            EndMs = line.EndMs,
            Text = line.Text,
        }).ToList();
}
