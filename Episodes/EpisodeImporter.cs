using EasyLangApi.Data;

namespace EasyLangApi.Episodes;

/// <summary>Сохраняет эпизод и его реплики одной транзакцией (один SaveChanges).</summary>
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
            Lines = lines.Select((line, index) => new Line
            {
                Index = index,
                StartMs = line.StartMs,
                EndMs = line.EndMs,
                Text = line.Text,
            }).ToList(),
        };

        db.Episodes.Add(episode);
        await db.SaveChangesAsync(cancellationToken);
        return episode;
    }
}
