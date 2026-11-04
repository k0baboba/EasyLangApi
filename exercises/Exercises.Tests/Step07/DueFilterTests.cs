using EasyLangApi.Tests.Infrastructure;
using Exercises.Infrastructure;

namespace Exercises.Step07;

/// <summary>Шаг 07. GET /vocabulary?due=true — только то, что пора повторить (DueAt не позже текущего времени).</summary>
public class DueFilterTests
{
    [Fact]
    public async Task Due_true_shows_only_items_whose_time_has_come()
    {
        await using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        await Scenario.AddPhrasesAsync(factory, client, "alpha", "bravo");

        // alpha старше, её покажут первой; верный ответ отодвигает её на сутки.
        Assert.Equal("alpha", await Scenario.ReviewNextAsync(client, wasCorrect: true));

        Assert.Equal(["bravo"], await Scenario.VocabularyPhrasesAsync(client, "?due=true"));
        Assert.Equal(["bravo", "alpha"], await Scenario.VocabularyPhrasesAsync(client));

        factory.Time.Advance(TimeSpan.FromDays(1));
        Assert.Equal(["bravo", "alpha"], await Scenario.VocabularyPhrasesAsync(client, "?due=true"));
    }

    [Fact]
    public async Task Due_combines_with_paging()
    {
        await using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        await Scenario.AddPhrasesAsync(factory, client, "alpha", "bravo", "charlie");
        await Scenario.ReviewNextAsync(client, wasCorrect: true); // alpha уходит на сутки

        Assert.Equal(["charlie"], await Scenario.VocabularyPhrasesAsync(client, "?due=true&take=1"));
        Assert.Equal(["bravo"], await Scenario.VocabularyPhrasesAsync(client, "?due=true&skip=1"));
    }
}
