using EasyLangApi.Data;
using EasyLangApi.Explaining;
using EasyLangApi.Recognition;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace EasyLangApi.Tests.Infrastructure;

/// <summary>
/// Поднимает приложение в памяти: своя временная база SQLite на каждый экземпляр,
/// подставные время и разборщик фраз.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"easylang-test-{Guid.NewGuid():N}.db");
    private readonly Dictionary<string, string?> _settings = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));

    public FakeExplainer Explainer { get; } = new();

    /// <summary>Заменять ли IPhraseExplainer подставным. false — проверяем настоящий выбор реализации.</summary>
    public bool UseFakeExplainer { get; init; } = true;

    /// <summary>Дополнительные подмены сервисов для конкретного теста.</summary>
    public Action<IServiceCollection>? ConfigureServices { get; init; }

    /// <summary>Если задан — подменяет Tesseract.</summary>
    public ITextRecognizer? Recognizer { get; init; }

    public ApiFactory WithSetting(string key, string? value)
    {
        _settings[key] = value;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_databasePath}");
        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            if (UseFakeExplainer)
                services.AddScoped<IPhraseExplainer>(_ => Explainer);
            if (Recognizer is not null)
                services.AddSingleton(Recognizer);
            ConfigureServices?.Invoke(services);
        });
    }

    public const string Password = "correct-horse-battery";

    /// <summary>Клиент, вошедший под новым пользователем (или под указанным email).</summary>
    public async Task<HttpClient> CreateUserClientAsync(string? email = null)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/auth/register",
            new { email = email ?? $"user-{Guid.NewGuid():N}@test.local", password = Password });
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        var tokens = await response.ReadJsonAsync();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens["accessToken"]!.GetValue<string>());
        return client;
    }

    /// <summary>
    /// Прямой доступ к базе — проверить, что именно сохранилось. Здесь нет HTTP-запроса и пользователя,
    /// поэтому фильтры по владельцу скрывают всё: в запросах нужен IgnoreQueryFilters().
    /// </summary>
    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }
}
