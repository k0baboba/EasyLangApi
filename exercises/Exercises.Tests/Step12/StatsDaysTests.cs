using System.Text.Json.Nodes;
using EasyLangApi.Tests.Infrastructure;
using Exercises.Infrastructure;

namespace Exercises.Step12;

/// <summary>
/// Шаг 12. В /stats поле days: ровно 7 дней по UTC, от самого старого к сегодняшнему,
/// для каждого дня — сколько ответов и сколько из них верных. Чужие ответы не считаются.
/// </summary>
public class StatsDaysTests
{
    [Fact]
    public async Task Seven_days_with_answers_per_day()
    {
        // Сутки, в течение которых access-токен должен жить.
        await using var factory = new ApiFactory().WithSetting("Jwt:AccessTokenMinutes", "20000");
        var client = await factory.CreateUserClientAsync(); // 2026-01-01 09:00 UTC
        await Scenario.AddPhrasesAsync(factory, client, "alpha", "bravo", "charlie");
        await Scenario.ReviewNextAsync(client, wasCorrect: true);  // 01.01
        await Scenario.ReviewNextAsync(client, wasCorrect: false); // 01.01
        factory.Time.Advance(TimeSpan.FromDays(2));
        await Scenario.ReviewNextAsync(client, wasCorrect: true);  // 03.01

        var days = (JsonArray)(await Scenario.GetJsonAsync(client, "/stats"))["days"]!;

        Assert.Equal(7, days.Count);
        Assert.Equal("2025-12-28", days[0]!["date"]!.GetValue<string>());
        Assert.Equal("2026-01-03", days[6]!["date"]!.GetValue<string>());
        var byDate = days.ToDictionary(d => d!["date"]!.GetValue<string>(),
            d => (d!["answers"]!.GetValue<int>(), d["correct"]!.GetValue<int>()));
        Assert.Equal((2, 1), byDate["2026-01-01"]);
        Assert.Equal((0, 0), byDate["2026-01-02"]);
        Assert.Equal((1, 1), byDate["2026-01-03"]);

        var stranger = await factory.CreateUserClientAsync();
        var strangerDays = (JsonArray)(await Scenario.GetJsonAsync(stranger, "/stats"))["days"]!;
        Assert.All(strangerDays, d => Assert.Equal(0, d!["answers"]!.GetValue<int>()));
    }
}
