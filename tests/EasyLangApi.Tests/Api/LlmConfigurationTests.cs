using System.Net;
using System.Text.Json;
using EasyLangApi.Explaining;
using EasyLangApi.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EasyLangApi.Tests.Api;

/// <summary>Выбор реализации по конфигурации и весь поток на подставном LLM API.</summary>
public class LlmConfigurationTests
{
    [Fact]
    public async Task With_api_key_llm_explainer_is_used_end_to_end()
    {
        var llm = new StubHttpHandler(_ => StubHttpHandler.Json(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { role = "assistant", content = """{"translation":"за счёт заведения","meaning":"Бесплатно."}""" } } },
        })));
        await using var factory = new ApiFactory
        {
            UseFakeExplainer = false,
            ConfigureServices = services =>
                services.AddHttpClient<LlmExplainer>().ConfigurePrimaryHttpMessageHandler(() => llm),
        };
        factory.WithSetting("Llm:ApiKey", "local-test-key")
            .WithSetting("Llm:BaseUrl", "http://localhost:11434/v1")
            .WithSetting("Llm:Model", "llama3.2");
        var client = factory.CreateClient();
        var episodeId = await client.UploadSampleAsync();

        var response = await client.ExplainAsync(await client.GetLineIdAsync(episodeId, 3), "on the house");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var explanation = (await response.ReadJsonAsync())["explanation"]!;
        Assert.Equal("llm", explanation["source"]!.GetValue<string>());
        Assert.Equal("за счёт заведения", explanation["translation"]!.GetValue<string>());

        var request = llm.Requests.Single();
        Assert.Equal("http://localhost:11434/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Contains("Night Shift S01E01", llm.RequestBodies.Single());
        Assert.Contains("llama3.2", llm.RequestBodies.Single());
    }

    [Fact]
    public async Task Empty_api_key_means_dictionary()
    {
        await using var factory = new ApiFactory { UseFakeExplainer = false }.WithSetting("Llm:ApiKey", "  ");
        using var scope = factory.Services.CreateScope();

        Assert.IsType<DictionaryExplainer>(scope.ServiceProvider.GetRequiredService<IPhraseExplainer>());
    }
}
