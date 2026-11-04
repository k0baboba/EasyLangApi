using System.Net;
using EasyLangApi.Explaining;
using Exercises.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Exercises.Step03;

/// <summary>Шаг 03. Временный сбой словаря (5xx) повторяется, и пользователь получает ответ.</summary>
public class RetryTests
{
    /// <summary>DictionaryExplainer из того же DI, что и в приложении, но с подставным HTTP.</summary>
    internal static DictionaryExplainer Create(ConcurrencyHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dictionary:BaseUrl"] = "http://dictionary.test/" })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddPhraseExplainers(configuration);
        services.AddHttpClient<DictionaryExplainer>().ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<DictionaryExplainer>();
    }

    [Fact]
    public async Task One_503_then_success_gives_explanation()
    {
        var calls = 0;
        var handler = new ConcurrencyHandler(request => Interlocked.Increment(ref calls) == 1
            ? (TimeSpan.Zero, ConcurrencyHandler.Status(HttpStatusCode.ServiceUnavailable))
            : (TimeSpan.Zero, ConcurrencyHandler.Entry(ConcurrencyHandler.Term(request))));

        var explanation = await Create(handler).ExplainAsync("beat", PhraseContext.Empty);

        Assert.True(explanation.Found);
        Assert.Equal("meaning of beat", explanation.Summary);
        Assert.Equal(2, calls);
    }
}
