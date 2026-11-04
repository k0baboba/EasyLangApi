using System.Net;
using EasyLangApi.Tests.Infrastructure;

namespace Exercises.Step20;

/// <summary>
/// Шаг 20. Настоящие файлы VTT: блоки NOTE и STYLE, идентификаторы реплик, настройки после таймингов,
/// теги вроде &lt;v Maya&gt; и &lt;i&gt;. Реплики из VTT разбираются с соседями, как реплики из SRT.
/// </summary>
public class WebVttFullTests
{
    private const string Vtt = """
        WEBVTT - Night Shift

        STYLE
        ::cue { color: yellow }

        NOTE This file was exported
        from a streaming service.

        intro
        00:00:01.000 --> 00:00:03.000 align:start position:10%
        <v Maya>Rain on the window again.</v>

        2
        00:04.000 --> 00:06.000
        <i>Double shift</i> at the docks.

        00:00:07.000 --> 00:00:09.000
        LEO: I'm beat.

        NOTE second note

        00:10.000 --> 00:12.000 line:90%
        MAYA: Coffee's on the house.

        00:00:13.000 --> 00:00:15.000
        You're a lifesaver.
        """;

    [Fact]
    public async Task Notes_styles_ids_settings_and_tags_are_handled()
    {
        await using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();

        var response = await client.UploadSrtAsync(Vtt, "night.vtt", title: "Night Shift");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var episodeId = (await response.ReadJsonAsync())["id"]!.GetValue<int>();

        var texts = (await client.GetLinesAsync(episodeId)).Select(l => l!["text"]!.GetValue<string>()).ToList();
        Assert.Equal(
        [
            "Rain on the window again.",
            "Double shift at the docks.",
            "LEO: I'm beat.",
            "MAYA: Coffee's on the house.",
            "You're a lifesaver.",
        ], texts);
    }

    [Fact]
    public async Task Vtt_lines_are_explained_with_neighbours()
    {
        await using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var response = await client.UploadSrtAsync(Vtt, "night.vtt", title: "Night Shift");
        var episodeId = (await response.ReadJsonAsync())["id"]!.GetValue<int>();

        var explain = await client.ExplainAsync(await client.GetLineIdAsync(episodeId, 2), "beat");

        Assert.Equal(HttpStatusCode.OK, explain.StatusCode);
        var (_, context) = Assert.Single(factory.Explainer.Calls);
        Assert.Equal("Night Shift", context.Title);
        Assert.Equal("LEO", context.Speaker);
        Assert.Equal(["Rain on the window again.", "Double shift at the docks."], context.Before);
        Assert.Equal(["MAYA: Coffee's on the house.", "You're a lifesaver."], context.After);
    }
}
