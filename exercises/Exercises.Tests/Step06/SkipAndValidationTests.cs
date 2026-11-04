using System.Net;
using EasyLangApi.Tests.Infrastructure;
using Exercises.Infrastructure;

namespace Exercises.Step06;

/// <summary>Шаг 06. skip пропускает элементы, а бессмысленные значения дают 400.</summary>
public class SkipAndValidationTests
{
    [Fact]
    public async Task Skip_and_take_give_a_page()
    {
        await using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        await Scenario.AddPhrasesAsync(factory, client, "alpha", "bravo", "charlie", "delta", "echo");

        Assert.Equal(["charlie", "bravo"], await Scenario.VocabularyPhrasesAsync(client, "?skip=2&take=2"));
        Assert.Equal(["alpha"], await Scenario.VocabularyPhrasesAsync(client, "?skip=4"));
        Assert.Empty(await Scenario.VocabularyPhrasesAsync(client, "?skip=10&take=3"));
    }

    [Theory]
    [InlineData("?take=0")]
    [InlineData("?take=-1")]
    [InlineData("?take=101")]
    [InlineData("?skip=-1")]
    public async Task Nonsense_paging_is_400(string query)
    {
        await using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();

        var response = await client.GetAsync("/vocabulary" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
