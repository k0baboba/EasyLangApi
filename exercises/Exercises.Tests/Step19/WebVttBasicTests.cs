using System.Net;
using EasyLangApi.Tests.Infrastructure;

namespace Exercises.Step19;

/// <summary>Шаг 19. Загрузка .vtt: заголовок WEBVTT, точка перед миллисекундами, часы необязательны.</summary>
public class WebVttBasicTests
{
    [Fact]
    public async Task Simple_vtt_becomes_lines_with_timings()
    {
        const string vtt = """
            WEBVTT

            00:01.000 --> 00:04.200
            Rain on the window.

            00:00:05.500 --> 00:00:07.000
            Coffee's on the house.
            Sit down.

            01:02:03.004 --> 01:02:04.000
            Last one.
            """;
        await using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();

        var response = await client.UploadSrtAsync(vtt, "scene.vtt");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var episodeId = (await response.ReadJsonAsync())["id"]!.GetValue<int>();
        var lines = (await client.GetLinesAsync(episodeId))
            .Select(l => (l!["startMs"]!.GetValue<int>(), l["endMs"]!.GetValue<int>(), l["text"]!.GetValue<string>()))
            .ToList();
        Assert.Equal(
        [
            (1000, 4200, "Rain on the window."),
            (5500, 7000, "Coffee's on the house. Sit down."),
            (3723004, 3724000, "Last one."),
        ], lines);
    }

    [Fact]
    public async Task Vtt_without_header_is_400()
    {
        await using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();

        var response = await client.UploadSrtAsync("00:01.000 --> 00:02.000\nHello.\n", "scene.vtt");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
