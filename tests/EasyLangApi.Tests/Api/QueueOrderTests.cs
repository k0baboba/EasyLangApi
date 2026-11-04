using System.Net;
using EasyLangApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EasyLangApi.Tests.Api;

/// <summary>
/// Порядок /review/next. Два элемента со сроком: A просрочен сильнее, но только что изучен (риск мал);
/// B просрочен меньше, но с последнего контакта прошло пять дней (риск велик).
/// По дате выбирается A, по вероятности забыть — B.
/// </summary>
public class QueueOrderTests
{
    private static async Task<string> NextPhraseAsync(ApiFactory factory)
    {
        var client = await factory.CreateUserClientAsync();
        var episodeId = await client.UploadSampleAsync();
        var lineId = await client.GetLineIdAsync(episodeId, 2);
        var a = (await (await client.ExplainAsync(lineId, "beat")).ReadJsonAsync())["knowledgeItemId"]!.GetValue<int>();
        var b = (await (await client.ExplainAsync(lineId, "shift")).ReadJsonAsync())["knowledgeItemId"]!.GetValue<int>();
        var now = factory.Time.GetUtcNow().UtcDateTime;

        await factory.WithDbAsync(async db =>
        {
            var items = await db.KnowledgeItems.IgnoreQueryFilters().Where(k => k.Id == a || k.Id == b).ToListAsync();
            var itemA = items.Single(k => k.Id == a);
            itemA.DueAt = now.AddDays(-10);
            itemA.FirstSeenAt = now.AddMinutes(-1);
            var itemB = items.Single(k => k.Id == b);
            itemB.DueAt = now.AddDays(-1);
            itemB.FirstSeenAt = now.AddDays(-5);
            return await db.SaveChangesAsync();
        });

        var next = await client.GetAsync("/review/next");
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        return (await next.ReadJsonAsync())["phrase"]!.GetValue<string>();
    }

    [Fact]
    public async Task Adaptive_scheduler_by_default_shows_the_item_most_likely_forgotten()
    {
        await using var factory = new ApiFactory().WithSetting("Review:Scheduler", "Adaptive");

        Assert.Equal("shift", await NextPhraseAsync(factory));
    }

    [Fact]
    public async Task Adaptive_scheduler_can_be_switched_back_to_due_date_order()
    {
        await using var factory = new ApiFactory()
            .WithSetting("Review:Scheduler", "Adaptive")
            .WithSetting("Review:QueueOrder", "DueDate");

        Assert.Equal("beat", await NextPhraseAsync(factory));
    }

    [Fact]
    public async Task Fixed_scheduler_keeps_due_date_order()
    {
        await using var factory = new ApiFactory();

        Assert.Equal("beat", await NextPhraseAsync(factory));
    }

    [Fact]
    public async Task Fixed_scheduler_ignores_forgetting_order_it_cannot_estimate()
    {
        await using var factory = new ApiFactory().WithSetting("Review:QueueOrder", "ForgettingProbability");

        Assert.Equal("beat", await NextPhraseAsync(factory));
    }
}
