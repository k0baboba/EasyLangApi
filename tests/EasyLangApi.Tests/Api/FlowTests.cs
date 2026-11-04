using System.Net;
using EasyLangApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EasyLangApi.Tests.Api;

/// <summary>Сквозной поток V1: субтитры → разбор с контекстом → словарь → повторение.</summary>
public class FlowTests
{
    [Fact]
    public async Task Full_flow_from_subtitles_to_review()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var episodeId = await client.UploadSampleAsync();
        var lineId = await client.GetLineIdAsync(episodeId, index: 2); // "LEO: I'm beat. ..."

        // 1. разбор
        var explain = await client.ExplainAsync(lineId, "beat");
        Assert.Equal(HttpStatusCode.OK, explain.StatusCode);
        var lookup = await explain.ReadJsonAsync();
        Assert.True(lookup["isNewItem"]!.GetValue<bool>());
        Assert.Equal("meaning of beat", lookup["explanation"]!["summary"]!.GetValue<string>());

        // 2. в разборщик ушёл контекст: название, говорящий, две реплики до и две после
        var (phrase, context) = Assert.Single(factory.Explainer.Calls);
        Assert.Equal("beat", phrase);
        Assert.Equal("Night Shift S01E01", context.Title);
        Assert.Equal("LEO", context.Speaker);
        Assert.Equal(2, context.Before!.Count);
        Assert.StartsWith("Rain on the window", context.Before[0]);
        Assert.Contains("I'm beat", context.CurrentLine);
        Assert.Equal(2, context.After!.Count);
        Assert.Contains("on the house", context.After[0]);

        // 3. словарь
        var vocabulary = await (await client.GetAsync("/vocabulary")).ReadJsonArrayAsync();
        var item = Assert.Single(vocabulary)!;
        Assert.Equal("beat", item["phrase"]!.GetValue<string>());
        Assert.Equal("meaning of beat", item["lastSeen"]!["summary"]!.GetValue<string>());

        // 4. повторение: новый элемент доступен сразу
        var next = await client.GetAsync("/review/next");
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        var card = await next.ReadJsonAsync();
        Assert.Equal("beat", card["phrase"]!.GetValue<string>());
        Assert.Contains("I'm beat", card["lineText"]!.GetValue<string>());

        var answer = await client.AnswerAsync(card["reviewId"]!.GetValue<int>(), wasCorrect: true);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(1, (await answer.ReadJsonAsync())["intervalDays"]!.GetValue<double>());

        // 5. до срока повторять нечего, после срока — снова есть
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/review/next")).StatusCode);
        factory.Time.Advance(TimeSpan.FromDays(1));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/review/next")).StatusCode);
    }

    [Fact]
    public async Task First_line_has_no_previous_context()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var episodeId = await client.UploadSampleAsync();

        await client.ExplainAsync(await client.GetLineIdAsync(episodeId, 0), "rain");

        var context = factory.Explainer.Calls.Single().Context;
        Assert.Empty(context.Before!);
        Assert.Equal(2, context.After!.Count);
        Assert.Null(context.Speaker);
    }

    [Fact]
    public async Task Same_phrase_in_other_form_reuses_knowledge_item()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var episodeId = await client.UploadSampleAsync();
        var lineId = await client.GetLineIdAsync(episodeId, 4);

        var first = await (await client.ExplainAsync(lineId, "spill the beans")).ReadJsonAsync();
        var second = await (await client.ExplainAsync(lineId, "Spill  the BEANS?")).ReadJsonAsync();

        Assert.Equal(first["knowledgeItemId"]!.GetValue<int>(), second["knowledgeItemId"]!.GetValue<int>());
        Assert.False(second["isNewItem"]!.GetValue<bool>());
        var item = Assert.Single(await (await client.GetAsync("/vocabulary")).ReadJsonArrayAsync())!;
        Assert.Equal(2, item["lookupCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task Every_show_is_kept_in_review_history()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var episodeId = await client.UploadSampleAsync();
        await client.ExplainAsync(await client.GetLineIdAsync(episodeId, 2), "beat");

        var firstShow = await (await client.GetAsync("/review/next")).ReadJsonAsync();
        var secondShow = await (await client.GetAsync("/review/next")).ReadJsonAsync();
        await client.AnswerAsync(secondShow["reviewId"]!.GetValue<int>(), wasCorrect: false);

        var events = await factory.WithDbAsync(db => db.ReviewEvents.OrderBy(e => e.Id).ToListAsync());
        Assert.Equal(2, events.Count);
        Assert.Equal(firstShow["reviewId"]!.GetValue<int>(), events[0].Id);
        Assert.Null(events[0].AnsweredAt);
        Assert.Null(events[0].WasCorrect);
        Assert.False(events[1].WasCorrect);
        Assert.NotNull(events[1].AnsweredAt);
    }

    [Fact]
    public async Task Answering_twice_is_conflict_and_unknown_review_is_not_found()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var episodeId = await client.UploadSampleAsync();
        await client.ExplainAsync(await client.GetLineIdAsync(episodeId, 2), "beat");
        var reviewId = (await (await client.GetAsync("/review/next")).ReadJsonAsync())["reviewId"]!.GetValue<int>();

        Assert.Equal(HttpStatusCode.OK, (await client.AnswerAsync(reviewId, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.AnswerAsync(reviewId, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.AnswerAsync(9999, true)).StatusCode);
    }

    [Fact]
    public async Task Answer_without_body_is_bad_request()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/review/1", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Review_is_empty_without_vocabulary()
    {
        await using var factory = new ApiFactory();

        var response = await factory.CreateClient().GetAsync("/review/next");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
