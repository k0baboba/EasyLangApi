using System.Net;
using EasyLangApi.Tests.Infrastructure;

namespace EasyLangApi.Tests.Api;

public class QuotaTests
{
    [Fact]
    public async Task Expensive_endpoints_are_rate_limited_per_user()
    {
        await using var factory = new ApiFactory { Recognizer = new FakeRecognizer("Text.") }
            .WithSetting("Quotas:ExpensiveRequestsPerMinute", "2");
        var alice = await factory.CreateUserClientAsync();
        var bob = await factory.CreateUserClientAsync();
        var episodeId = await alice.UploadSampleAsync();
        var lineId = await alice.GetLineIdAsync(episodeId, 2);

        Assert.Equal(HttpStatusCode.OK, (await alice.ExplainAsync(lineId, "beat")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.RecognizeAsync(TextImages.Render("x"))).StatusCode);
        var limited = await alice.ExplainAsync(lineId, "beat");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("Слишком много запросов", (await limited.ReadJsonAsync())["title"]!.GetValue<string>());
        // Дешёвые эндпоинты лимит не трогает, у другого пользователя своё окно
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync("/vocabulary")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.RecognizeAsync(TextImages.Render("x"))).StatusCode);
    }

    [Fact]
    public async Task Daily_lookup_quota_resets_next_day()
    {
        await using var factory = new ApiFactory()
            .WithSetting("Quotas:LookupsPerDay", "1")
            .WithSetting("Jwt:AccessTokenMinutes", "2880");
        var client = await factory.CreateUserClientAsync();
        var episodeId = await client.UploadSampleAsync();
        var lineId = await client.GetLineIdAsync(episodeId, 2);

        Assert.Equal(HttpStatusCode.OK, (await client.ExplainAsync(lineId, "beat")).StatusCode);
        var exceeded = await client.ExplainAsync(lineId, "double shift");

        Assert.Equal(HttpStatusCode.TooManyRequests, exceeded.StatusCode);
        Assert.Single(factory.Explainer.Calls); // до разборщика запрос не дошёл

        factory.Time.Advance(TimeSpan.FromDays(1));
        Assert.Equal(HttpStatusCode.OK, (await client.ExplainAsync(lineId, "double shift")).StatusCode);
    }
}
