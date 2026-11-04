using Microsoft.Extensions.Http.Resilience;
using Polly;
using Microsoft.Extensions.Options;

namespace EasyLangApi.Explaining;

public static class ExplainingServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует обе реализации и выбирает одну на каждый запрос:
    /// задан Llm:ApiKey — языковая модель, иначе — словарь.
    /// </summary>
    public static IServiceCollection AddPhraseExplainers(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DictionaryOptions>(configuration.GetSection(DictionaryOptions.SectionName));
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));

        services.AddHttpClient<DictionaryExplainer>((sp, http) =>
        {
            var settings = sp.GetRequiredService<IOptions<DictionaryOptions>>().Value;
            http.BaseAddress = WithTrailingSlash(settings.BaseUrl);
            http.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("EasyLangApi/1.0");
        })
        // Временные сбои (5xx, 408, 429, обрыв) повторяем: всего три попытки с короткой
        // растущей задержкой. 404 и 400 стратегия по умолчанию не повторяет.
        .AddResilienceHandler("dictionary-retry", pipeline => pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            Delay = TimeSpan.FromMilliseconds(200),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
        }));

        services.AddHttpClient<LlmExplainer>((sp, http) =>
        {
            var settings = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            http.BaseAddress = WithTrailingSlash(settings.BaseUrl);
            http.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        });

        // С ключом LLM — языковая модель, а при её недоступности словарь (декоратор).
        services.AddScoped<IPhraseExplainer>(sp =>
            sp.GetRequiredService<IOptions<LlmOptions>>().Value.IsEnabled
                ? new FallbackExplainer(
                    sp.GetRequiredService<LlmExplainer>(),
                    sp.GetRequiredService<DictionaryExplainer>(),
                    sp.GetRequiredService<ILogger<FallbackExplainer>>())
                : sp.GetRequiredService<DictionaryExplainer>());

        return services;
    }

    // Без завершающего "/" относительный путь заменил бы последний сегмент BaseAddress.
    private static Uri WithTrailingSlash(string url) => new(url.EndsWith('/') ? url : url + "/");
}
