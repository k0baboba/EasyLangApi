using EasyLangApi.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EasyLangApi.Data;

/// <summary>
/// Данные разделены по пользователям глобальными фильтрами запросов: каждый запрос к эпизодам,
/// репликам, наблюдениям, словарю и истории сам добавляет условие "владелец — текущий пользователь".
/// Забыть Where(... UserId ...) в отдельном запросе так нельзя. Без пользователя (null) не видно ничего.
/// Для служебного доступа ко всем данным — IgnoreQueryFilters().
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser) : DbContext(options)
{
    // EF подставляет значение этого свойства в фильтры при каждом запросе.
    private int? CurrentUserId => currentUser.UserId;

    public DbSet<Episode> Episodes => Set<Episode>();
    public DbSet<Line> Lines => Set<Line>();
    public DbSet<Lookup> Lookups => Set<Lookup>();
    public DbSet<KnowledgeItem> KnowledgeItems => Set<KnowledgeItem>();
    public DbSet<ReviewEvent> ReviewEvents => Set<ReviewEvent>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(token =>
        {
            token.HasIndex(t => t.TokenHash).IsUnique();
        });

        modelBuilder.Entity<Episode>(episode =>
        {
            episode.HasOne<User>().WithMany().HasForeignKey(e => e.UserId);
            episode.HasIndex(e => e.UserId);
            episode.HasQueryFilter(e => e.UserId == CurrentUserId);
        });

        modelBuilder.Entity<Line>(line =>
        {
            // Соседние реплики ищем по (EpisodeId, Index) — отсюда составной индекс.
            line.HasIndex(l => new { l.EpisodeId, l.Index }).IsUnique();
            line.HasQueryFilter(l => l.Episode.UserId == CurrentUserId);
        });

        modelBuilder.Entity<KnowledgeItem>(item =>
        {
            item.HasOne<User>().WithMany().HasForeignKey(k => k.UserId);
            // Одна и та же фраза у разных пользователей — разные элементы.
            item.HasIndex(k => new { k.UserId, k.PhraseNormalized }).IsUnique();
            // /review/next ищет самый просроченный элемент пользователя.
            item.HasIndex(k => new { k.UserId, k.DueAt });
            item.HasQueryFilter(k => k.UserId == CurrentUserId);
        });

        modelBuilder.Entity<Lookup>(lookup =>
        {
            lookup.HasQueryFilter(l => l.KnowledgeItem.UserId == CurrentUserId);
        });

        modelBuilder.Entity<ReviewEvent>(ev =>
        {
            // История ответов не должна пропадать каскадом вместе с элементом.
            ev.HasOne(e => e.KnowledgeItem)
                .WithMany(k => k.ReviewEvents)
                .HasForeignKey(e => e.KnowledgeItemId)
                .OnDelete(DeleteBehavior.Restrict);
            ev.HasQueryFilter(e => e.KnowledgeItem.UserId == CurrentUserId);
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
