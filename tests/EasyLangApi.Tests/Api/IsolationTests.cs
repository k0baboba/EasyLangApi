using System.Net;
using EasyLangApi.Tests.Infrastructure;

namespace EasyLangApi.Tests.Api;

/// <summary>Данные одного пользователя не видны и недоступны другому.</summary>
public class IsolationTests
{
    [Fact]
    public async Task Other_users_data_is_invisible_and_unreachable()
    {
        await using var factory = new ApiFactory { Recognizer = new FakeRecognizer("Screen text.") };
        var alice = await factory.CreateUserClientAsync("alice@example.com");
        var bob = await factory.CreateUserClientAsync("bob@example.com");

        var episodeId = await alice.UploadSampleAsync();
        var lineId = await alice.GetLineIdAsync(episodeId, 2);
        var lookup = await (await alice.ExplainAsync(lineId, "beat")).ReadJsonAsync();
        var itemId = lookup["knowledgeItemId"]!.GetValue<int>();
        var reviewId = (await (await alice.GetAsync("/review/next")).ReadJsonAsync())["reviewId"]!.GetValue<int>();

        // Списки у Боба пустые
        Assert.Empty(await (await bob.GetAsync("/episodes")).ReadJsonArrayAsync());
        Assert.Empty(await (await bob.GetAsync("/vocabulary")).ReadJsonArrayAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await bob.GetAsync("/review/next")).StatusCode);

        // Прямые ссылки на чужое — 404, как будто этого нет
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/episodes/{episodeId}/lines")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.ExplainAsync(lineId, "beat")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/vocabulary/{itemId}/examples")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.AnswerAsync(reviewId, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await bob.RecognizeAsync(TextImages.Render("x"), episodeId: episodeId)).StatusCode);

        // У Алисы всё на месте, и ответ Боба ничего не испортил
        Assert.Equal(HttpStatusCode.OK, (await alice.AnswerAsync(reviewId, true)).StatusCode);
    }

    [Fact]
    public async Task Same_phrase_is_tracked_separately_per_user()
    {
        await using var factory = new ApiFactory();
        var alice = await factory.CreateUserClientAsync();
        var bob = await factory.CreateUserClientAsync();

        var aliceItem = await ExplainBeat(alice);
        var bobItem = await ExplainBeat(bob);

        Assert.NotEqual(aliceItem["knowledgeItemId"]!.GetValue<int>(), bobItem["knowledgeItemId"]!.GetValue<int>());
        Assert.True(bobItem["isNewItem"]!.GetValue<bool>());
        Assert.Single(await (await alice.GetAsync("/vocabulary")).ReadJsonArrayAsync());
        Assert.Single(await (await bob.GetAsync("/vocabulary")).ReadJsonArrayAsync());
    }

    [Fact]
    public async Task Review_selection_ignores_other_users_vocabulary()
    {
        await using var factory = new ApiFactory();
        var alice = await factory.CreateUserClientAsync();
        var bob = await factory.CreateUserClientAsync();
        const string srt = "1\n00:00:01,000 --> 00:00:02,000\nNo cap, he's beat.\n\n2\n00:00:03,000 --> 00:00:04,000\nI'm beat.\n";

        // У Боба "no cap" не выучено, но это не должно влиять на Алису
        var bobEpisode = (await (await bob.UploadSrtAsync(srt)).ReadJsonAsync())["id"]!.GetValue<int>();
        await bob.ExplainAsync(await bob.GetLineIdAsync(bobEpisode, 0), "no cap");
        var aliceEpisode = (await (await alice.UploadSrtAsync(srt)).ReadJsonAsync())["id"]!.GetValue<int>();
        await alice.ExplainAsync(await alice.GetLineIdAsync(aliceEpisode, 1), "beat");

        var examples = await (await alice.GetAsync("/vocabulary")).ReadJsonArrayAsync();
        var itemId = examples.Single()!["id"]!.GetValue<int>();
        var lines = await (await alice.GetAsync($"/vocabulary/{itemId}/examples")).ReadJsonArrayAsync();

        Assert.Equal(2, lines.Count); // только реплики Алисы
        Assert.All(lines, l => Assert.True(l!["isSingleUnknown"]!.GetValue<bool>()));
    }

    private static async Task<System.Text.Json.Nodes.JsonObject> ExplainBeat(HttpClient client)
    {
        var episodeId = await client.UploadSampleAsync();
        return await (await client.ExplainAsync(await client.GetLineIdAsync(episodeId, 2), "beat")).ReadJsonAsync();
    }
}
