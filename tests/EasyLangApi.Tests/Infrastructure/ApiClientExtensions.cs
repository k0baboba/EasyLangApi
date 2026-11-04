using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EasyLangApi.Tests.Infrastructure;

public static class ApiClientExtensions
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<HttpResponseMessage> UploadSrtAsync(
        this HttpClient client, string content, string fileName = "episode.srt", string? title = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/x-subrip");
        form.Add(file, "file", fileName);
        if (title is not null)
            form.Add(new StringContent(title), "title");
        return await client.PostAsync("/episodes", form);
    }

    /// <summary>Загружает sample.srt и возвращает id эпизода.</summary>
    public static async Task<int> UploadSampleAsync(this HttpClient client, string title = "Night Shift S01E01")
    {
        var content = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "sample.srt"));
        var response = await client.UploadSrtAsync(content, "sample.srt", title);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.ReadJsonAsync();
        return body["id"]!.GetValue<int>();
    }

    public static async Task<JsonArray> GetLinesAsync(this HttpClient client, int episodeId)
    {
        var response = await client.GetAsync($"/episodes/{episodeId}/lines");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (JsonArray)(await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync()))!;
    }

    /// <summary>id реплики по её порядковому номеру в эпизоде.</summary>
    public static async Task<int> GetLineIdAsync(this HttpClient client, int episodeId, int index)
    {
        var lines = await client.GetLinesAsync(episodeId);
        return lines.Single(l => l!["index"]!.GetValue<int>() == index)!["id"]!.GetValue<int>();
    }

    public static async Task<HttpResponseMessage> RecognizeAsync(
        this HttpClient client, byte[] image, string fileName = "screen.png", string? title = null, int? episodeId = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(image);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "image", fileName);
        if (title is not null)
            form.Add(new StringContent(title), "title");
        if (episodeId is not null)
            form.Add(new StringContent(episodeId.Value.ToString()), "episodeId");
        return await client.PostAsync("/recognize", form);
    }

    public static Task<HttpResponseMessage> ExplainAsync(this HttpClient client, int lineId, string phrase) =>
        client.GetAsync($"/lines/{lineId}/explain?phrase={Uri.EscapeDataString(phrase)}");

    public static Task<HttpResponseMessage> AnswerAsync(this HttpClient client, int reviewId, bool wasCorrect) =>
        client.PostAsJsonAsync($"/review/{reviewId}", new { wasCorrect });

    public static async Task<JsonObject> ReadJsonAsync(this HttpResponseMessage response)
    {
        var node = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
        return (JsonObject)node!;
    }

    public static async Task<JsonArray> ReadJsonArrayAsync(this HttpResponseMessage response)
    {
        var node = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
        return (JsonArray)node!;
    }
}
