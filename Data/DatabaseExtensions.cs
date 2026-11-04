using Microsoft.EntityFrameworkCore;

namespace EasyLangApi.Data;

public static class DatabaseExtensions
{
    /// <summary>Создаёт файл базы и применяет миграции при старте приложения.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }
}
