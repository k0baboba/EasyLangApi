using System.Data.Common;
using EasyLangApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Exercises.Step15;

/// <summary>
/// Шаг 15. Суточная квота считает Lookup пользователя с CreatedAt за сегодня. Нужен индекс
/// Lookups(KnowledgeItemId, CreatedAt) — в модели EF и в настоящей базе через миграцию.
/// </summary>
public class QuotaIndexTests
{
    [Fact]
    public async Task Database_has_index_on_KnowledgeItemId_then_CreatedAt()
    {
        await using var factory = new ApiFactory();
        _ = factory.CreateClient(); // старт приложения применяет миграции

        var (columns, pending, modelChanged) = await factory.WithDbAsync(async db =>
        {
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            var indexes = await QueryAsync(connection, "SELECT name FROM pragma_index_list('Lookups')");
            var result = new List<string>();
            foreach (var index in indexes)
                result.Add(string.Join(",", await QueryAsync(connection, $"SELECT name FROM pragma_index_info('{index}') ORDER BY seqno")));
            return (result, (await db.Database.GetPendingMigrationsAsync()).ToList(), db.Database.HasPendingModelChanges());
        });

        Assert.Contains("KnowledgeItemId,CreatedAt", columns);
        Assert.Empty(pending);
        Assert.False(modelChanged, "Модель EF изменилась, а миграции нет: dotnet ef migrations add ...");
    }

    private static async Task<List<string>> QueryAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
            values.Add(reader.GetString(0));
        return values;
    }
}
