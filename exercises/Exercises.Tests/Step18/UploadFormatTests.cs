using System.Net;
using EasyLangApi.Tests.Infrastructure;

namespace Exercises.Step18;

/// <summary>Шаг 18. POST /episodes выбирает разборщик по расширению файла; неизвестное расширение — 400.</summary>
public class UploadFormatTests
{
    private const string Srt = "1\n00:00:01,000 --> 00:00:02,000\nHello.\n";

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("episode")]
    [InlineData("movie.ass")]
    public async Task Unknown_extension_is_400(string fileName)
    {
        await using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();

        var response = await client.UploadSrtAsync(Srt, fileName);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("episode.srt")]
    [InlineData("EPISODE.SRT")]
    public async Task Srt_still_works(string fileName)
    {
        await using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();

        var response = await client.UploadSrtAsync(Srt, fileName);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
