using System.Net;
using System.Text.Json.Nodes;
using EasyLangApi.Tests.Infrastructure;

namespace Exercises.Infrastructure;

/// <summary>Короткие сценарии поверх API, общие для шагов.</summary>
public static class Scenario
{
    /// <summary>Загружает sample.srt и разбирает фразы в реплике 2. Между фразами сдвигает часы на минуту.</summary>
    public static async Task<List<int>> AddPhrasesAsync(ApiFactory factory, HttpClient client, params string[] phrases)
    {
        var episodeId = await client.UploadSampleAsync();
        var lineId = await client.GetLineIdAsync(episodeId, 2);
        var ids = new List<int>();
        foreach (var phrase in phrases)
        {
            var response = await client.ExplainAsync(lineId, phrase);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            ids.Add((await response.ReadJsonAsync())["knowledgeItemId"]!.GetValue<int>());
            factory.Time.Advance(TimeSpan.FromMinutes(1));
        }

        return ids;
    }

    public static async Task<List<string>> VocabularyPhrasesAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync("/vocabulary" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadJsonArrayAsync()).Select(i => i!["phrase"]!.GetValue<string>()).ToList();
    }

    /// <summary>Берёт следующую карточку и отвечает на неё. Возвращает фразу карточки.</summary>
    public static async Task<string> ReviewNextAsync(HttpClient client, bool wasCorrect)
    {
        var next = await client.GetAsync("/review/next");
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        var card = await next.ReadJsonAsync();
        var answer = await client.AnswerAsync(card["reviewId"]!.GetValue<int>(), wasCorrect);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        return card["phrase"]!.GetValue<string>();
    }

    public static async Task<JsonObject> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }
}
