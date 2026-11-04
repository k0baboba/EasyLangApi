using System.Net;
using EasyLangApi.Tests.Infrastructure;
using Exercises.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Exercises.Step10;

/// <summary>Шаг 10. После гонки в базе один элемент словаря и оба наблюдения, а «новым» элемент назван ровно в одном ответе.</summary>
public class ParallelNewPhraseDataTests
{
    [Fact]
    public async Task One_item_two_lookups_one_new()
    {
        SaveChangesBarrier barrier = null!;
        await using var factory = new ApiFactory { ConfigureServices = services => barrier = SaveChangesBarrier.AddTo(services) };
        var client = await factory.CreateUserClientAsync();
        var episodeId = await client.UploadSampleAsync();
        var lineId = await client.GetLineIdAsync(episodeId, 2);

        barrier.Arm(participants: 2);
        var responses = await Task.WhenAll(client.ExplainAsync(lineId, "beat"), client.ExplainAsync(lineId, "Beat!"));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var bodies = await Task.WhenAll(responses.Select(r => r.ReadJsonAsync()));
        Assert.Single(bodies, b => b["isNewItem"]!.GetValue<bool>());
        Assert.Single(bodies.Select(b => b["knowledgeItemId"]!.GetValue<int>()).Distinct());

        var (items, lookups) = await factory.WithDbAsync(async db => (
            await db.KnowledgeItems.IgnoreQueryFilters().CountAsync(),
            await db.Lookups.IgnoreQueryFilters().CountAsync()));
        Assert.Equal(1, items);
        Assert.Equal(2, lookups);
    }
}
