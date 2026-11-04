using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EasyLangApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Episode> Episodes => Set<Episode>();
    public DbSet<Line> Lines => Set<Line>();
    public DbSet<Lookup> Lookups => Set<Lookup>();
    public DbSet<KnowledgeItem> KnowledgeItems => Set<KnowledgeItem>();
    public DbSet<ReviewEvent> ReviewEvents => Set<ReviewEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Line>(line =>
        {
            // Соседние реплики ищем по (EpisodeId, Index) — отсюда составной индекс.
            line.HasIndex(l => new { l.EpisodeId, l.Index }).IsUnique();
        });

        modelBuilder.Entity<KnowledgeItem>(item =>
        {
            item.HasIndex(k => k.PhraseNormalized).IsUnique();
            // /review/next ищет самый просроченный элемент.
            item.HasIndex(k => k.DueAt);
        });

        modelBuilder.Entity<ReviewEvent>(ev =>
        {
            // История ответов не должна пропадать каскадом вместе с элементом.
            ev.HasOne(e => e.KnowledgeItem)
                .WithMany(k => k.ReviewEvents)
                .HasForeignKey(e => e.KnowledgeItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite не хранит DateTimeKind. Всё время в приложении — UTC,
        // поэтому при чтении помечаем значения как UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        toDb => toDb.Kind == DateTimeKind.Utc ? toDb : toDb.ToUniversalTime(),
        fromDb => DateTime.SpecifyKind(fromDb, DateTimeKind.Utc));
}
