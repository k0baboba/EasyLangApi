using EasyLangApi.Tests.Infrastructure;
using Exercises.Infrastructure;

namespace Exercises.Step05;

/// <summary>Шаг 05. GET /vocabulary?take=N отдаёт не больше N элементов, сначала новые.</summary>
public class TakeTests
{
    [Fact]
    public async Task Take_limits_the_list()
    {
        await using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        await Scenario.AddPhrasesAsync(factory, client, "alpha", "bravo", "charlie", "delta", "echo");

        Assert.Equal(["echo", "delta"], await Scenario.VocabularyPhrasesAsync(client, "?take=2"));
        // Без параметров — по-прежнему весь словарь.
        Assert.Equal(5, (await Scenario.VocabularyPhrasesAsync(client)).Count);
    }
}
