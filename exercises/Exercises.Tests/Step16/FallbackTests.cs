using System.Net;
using EasyLangApi.Explaining;
using EasyLangApi.Tests.Infrastructure;
using Exercises.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Exercises.Step16;

/// <summary>Шаг 16. Если языковая модель недоступна, разбор делает словарь. LookupService при этом не меняется.</summary>
public class FallbackTests
{
    internal static ApiFactory Factory(HttpStatusCode llmStatus, Action<IServiceCollection>? more = null)
    {
        var llm = new ConcurrencyHandler(_ => (TimeSpan.Zero, ConcurrencyHandler.Status(llmStatus)));
        var dictionary = new ConcurrencyHandler(request =>
            (TimeSpan.Zero, ConcurrencyHandler.Entry(ConcurrencyHandler.Term(request))));
        var factory = new ApiFactory
        {
            UseFakeExplainer = false,
            ConfigureServices = services =>
            {
                services.AddHttpClient<LlmExplainer>().ConfigurePrimaryHttpMessageHandler(() => llm);
                services.AddHttpClient<DictionaryExplainer>().ConfigurePrimaryHttpMessageHandler(() => dictionary);
                more?.Invoke(services);
            },
        };
        factory.WithSetting("Llm:ApiKey", "test-key")
            .WithSetting("Llm:BaseUrl", "http://llm.test/v1")
            .WithSetting("Dictionary:BaseUrl", "http://dictionary.test/");
        return factory;
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Unavailable_llm_falls_back_to_dictionary(HttpStatusCode llmStatus)
    {
        await using var factory = Factory(llmStatus);
        var client = await factory.CreateUserClientAsync();
        var lineId = await client.GetLineIdAsync(await client.UploadSampleAsync(), 2);

        var response = await client.ExplainAsync(lineId, "beat");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var explanation = (await response.ReadJsonAsync())["explanation"]!;
        Assert.Equal("dictionary", explanation["source"]!.GetValue<string>());
        Assert.Equal("meaning of beat", explanation["summary"]!.GetValue<string>());
    }

    [Fact]
    public async Task Without_llm_key_dictionary_is_used_directly()
    {
        await using var factory = new ApiFactory { UseFakeExplainer = false }.WithSetting("Llm:ApiKey", "");
        using var scope = factory.Services.CreateScope();

        Assert.IsType<DictionaryExplainer>(scope.ServiceProvider.GetRequiredService<IPhraseExplainer>());
    }
}
