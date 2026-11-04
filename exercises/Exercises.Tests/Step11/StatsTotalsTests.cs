using EasyLangApi.Tests.Infrastructure;
using Exercises.Infrastructure;

namespace Exercises.Step11;

/// <summary>Шаг 11. GET /stats: сколько элементов в словаре и сколько из них выучено (IntervalDays >= Review:KnownAfterDays).</summary>
public class StatsTotalsTests
{
    [Fact]
    public async Task Totals_and_known()
    {
        // Порог «выучено» — 1 день, чтобы одного верного ответа хватило.
        await using var factory = new ApiFactory().WithSetting("Review:KnownAfterDays", "1");
        var client = await factory.CreateUserClientAsync();
        await Scenario.AddPhrasesAsync(factory, client, "alpha", "bravo", "charlie");
        await Scenario.ReviewNextAsync(client, wasCorrect: true);  // alpha: интервал 1 день — выучено
        await Scenario.ReviewNextAsync(client, wasCorrect: false); // bravo: тоже 1 день по лестнице

        var stats = await Scenario.GetJsonAsync(client, "/stats");

        Assert.Equal(3, stats["totalItems"]!.GetValue<int>());
        Assert.Equal(2, stats["knownItems"]!.GetValue<int>());
    }

    [Fact]
    public async Task Empty_dictionary_gives_zeros()
    {
        await using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();

        var stats = await Scenario.GetJsonAsync(client, "/stats");

        Assert.Equal(0, stats["totalItems"]!.GetValue<int>());
        Assert.Equal(0, stats["knownItems"]!.GetValue<int>());
    }
}
