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
        });

        services.AddHttpClient<LlmExplainer>((sp, http) =>
        {
            var settings = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            http.BaseAddress = WithTrailingSlash(settings.BaseUrl);
            http.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        });

        services.AddScoped<IPhraseExplainer>(sp =>
            sp.GetRequiredService<IOptions<LlmOptions>>().Value.IsEnabled
                ? sp.GetRequiredService<LlmExplainer>()
                : sp.GetRequiredService<DictionaryExplainer>());

        return services;
    }

    // Без завершающего "/" относительный путь заменил бы последний сегмент BaseAddress.
    private static Uri WithTrailingSlash(string url) => new(url.EndsWith('/') ? url : url + "/");
}
