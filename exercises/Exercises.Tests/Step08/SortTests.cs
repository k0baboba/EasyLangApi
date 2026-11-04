using System.Net;
using EasyLangApi.Tests.Infrastructure;
using Exercises.Infrastructure;

namespace Exercises.Step08;

/// <summary>Шаг 08. GET /vocabulary?sort=recent|phrase|due. По умолчанию recent — как раньше.</summary>
public class SortTests
{
    [Fact]
    public async Task Sort_by_phrase_due_and_recent()
    {
        await using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        await Scenario.AddPhrasesAsync(factory, client, "charlie", "alpha", "bravo");
        // charlie — самая старая, её первой покажут на повторение; верный ответ отодвигает её на сутки.
        Assert.Equal("charlie", await Scenario.ReviewNextAsync(client, wasCorrect: true));

        Assert.Equal(["alpha", "bravo", "charlie"], await Scenario.VocabularyPhrasesAsync(client, "?sort=phrase"));
        Assert.Equal(["alpha", "bravo", "charlie"], await Scenario.VocabularyPhrasesAsync(client, "?sort=due"));
        Assert.Equal(["bravo", "alpha", "charlie"], await Scenario.VocabularyPhrasesAsync(client, "?sort=recent"));
        Assert.Equal(["bravo", "alpha", "charlie"], await Scenario.VocabularyPhrasesAsync(client));
        Assert.Equal(["bravo"], await Scenario.VocabularyPhrasesAsync(client, "?sort=phrase&skip=1&take=1"));
    }

    [Fact]
    public async Task Unknown_sort_is_400()
    {
        await using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();

        var response = await client.GetAsync("/vocabulary?sort=random");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
