using System.Net;
using EasyLangApi.Tests.Infrastructure;
using EasyLangApi.Explaining;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyLangApi.Tests.Api;

public class ValidationTests
{
    [Fact]
    public async Task Upload_without_subtitles_is_rejected()
    {
        await using var factory = new ApiFactory();

        var response = await (await factory.CreateUserClientAsync()).UploadSrtAsync("not a subtitle file at all");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_without_file_is_rejected()
    {
        await using var factory = new ApiFactory();
        using var form = new MultipartFormDataContent { { new StringContent("title"), "title" } };

        var response = await (await factory.CreateUserClientAsync()).PostAsync("/episodes", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Title_defaults_to_file_name()
    {
        await using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();

        var response = await client.UploadSrtAsync("1\n00:00:01,000 --> 00:00:02,000\nHi\n", "Pilot.srt");

        Assert.Equal("Pilot", (await response.ReadJsonAsync())["title"]!.GetValue<string>());
        Assert.Single(await (await client.GetAsync("/episodes")).ReadJsonArrayAsync());
    }

    [Fact]
    public async Task Lines_of_missing_episode_are_not_found()
    {
        await using var factory = new ApiFactory();

        var response = await (await factory.CreateUserClientAsync()).GetAsync("/episodes/42/lines");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Explain_unknown_line_is_not_found()
    {
        await using var factory = new ApiFactory();

        var response = await (await factory.CreateUserClientAsync()).ExplainAsync(12345, "beat");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ?! ")]
    public async Task Explain_without_words_is_bad_request(string phrase)
    {
        await using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var episodeId = await client.UploadSampleAsync();

        var response = await client.ExplainAsync(await client.GetLineIdAsync(episodeId, 2), phrase);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Explainer.Calls);
    }

    [Fact]
    public async Task Unavailable_explainer_gives_503_and_saves_nothing()
    {
        await using var factory = new ApiFactory();
        factory.Explainer.Unavailable = true;
        var client = await factory.CreateUserClientAsync();
        var episodeId = await client.UploadSampleAsync();

        var response = await client.ExplainAsync(await client.GetLineIdAsync(episodeId, 2), "beat");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(0, await factory.WithDbAsync(db => db.Lookups.IgnoreQueryFilters().CountAsync()));
        Assert.Equal(0, await factory.WithDbAsync(db => db.KnowledgeItems.IgnoreQueryFilters().CountAsync()));
    }

    [Fact]
    public async Task Without_llm_key_dictionary_explainer_is_used()
    {
        await using var factory = new ApiFactory { UseFakeExplainer = false };

        using var scope = factory.Services.CreateScope();

        var explainer = scope.ServiceProvider.GetRequiredService<IPhraseExplainer>();

        Assert.IsType<DictionaryExplainer>(explainer);
    }
}
