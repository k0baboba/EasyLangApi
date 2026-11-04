using System.Net;
using EasyLangApi.Tests.Infrastructure;
using EasyLangApi.Vocabulary;

namespace EasyLangApi.Tests.Api;

public class RecognitionTests
{
    [Fact]
    public async Task Recognized_text_goes_into_the_same_explain_flow()
    {
        Tesseract.SkipIfMissing();
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var image = TextImages.Render("I'm beat. Double shift\nat the docks again.", "Coffee is on the house tonight.");

        var response = await client.RecognizeAsync(image, title: "Harbor Lights");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Contains("on the house", PhraseNormalizer.Normalize(body["text"]!.GetValue<string>()));
        var lines = body["lines"]!.AsArray();
        Assert.Equal(2, lines.Count);

        var explain = await client.ExplainAsync(lines[1]!["id"]!.GetValue<int>(), "on the house");
        Assert.Equal(HttpStatusCode.OK, explain.StatusCode);
        var context = factory.Explainer.Calls.Single().Context;
        Assert.Equal("Harbor Lights", context.Title);
        Assert.Contains("docks", Assert.Single(context.Before!));
    }

    [Fact]
    public async Task Without_tesseract_app_works_and_endpoint_explains_why()
    {
        await using var factory = new ApiFactory().WithSetting("Ocr:TesseractPath", "/nonexistent/tesseract");
        var client = factory.CreateClient();

        var response = await client.RecognizeAsync(TextImages.Render("hello"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.ReadJsonAsync();
        Assert.Equal("Распознавание текста недоступно", problem["title"]!.GetValue<string>());
        Assert.Contains("Tesseract не найден", problem["detail"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/episodes")).StatusCode);
    }

    [Fact]
    public async Task Not_an_image_is_rejected_before_recognition()
    {
        var recognizer = new FakeRecognizer("never");
        await using var factory = new ApiFactory { Recognizer = recognizer };

        var response = await factory.CreateClient().RecognizeAsync("plain text"u8.ToArray(), "notes.txt");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, recognizer.Calls);
    }

    [Fact]
    public async Task Screenshots_can_be_appended_to_one_episode()
    {
        await using var factory = new ApiFactory { Recognizer = new FakeRecognizer("First box.\n\nSecond box.") };
        var client = factory.CreateClient();

        var first = await (await client.RecognizeAsync(TextImages.Render("x"), title: "Some Game")).ReadJsonAsync();
        var episodeId = first["episodeId"]!.GetValue<int>();
        var second = await (await client.RecognizeAsync(TextImages.Render("x"), episodeId: episodeId)).ReadJsonAsync();

        Assert.Equal(episodeId, second["episodeId"]!.GetValue<int>());
        Assert.Equal([2, 3], second["lines"]!.AsArray().Select(l => l!["index"]!.GetValue<int>()));

        await client.ExplainAsync(second["lines"]![0]!["id"]!.GetValue<int>(), "box");
        var context = factory.Explainer.Calls.Single().Context;
        Assert.Equal(["First box.", "Second box."], context.Before!);
    }

    [Fact]
    public async Task Appending_to_missing_episode_is_not_found()
    {
        await using var factory = new ApiFactory { Recognizer = new FakeRecognizer("Text.") };

        var response = await factory.CreateClient().RecognizeAsync(TextImages.Render("x"), episodeId: 999);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Image_without_text_creates_no_episode()
    {
        await using var factory = new ApiFactory { Recognizer = new FakeRecognizer("   ") };
        var client = factory.CreateClient();

        var body = await (await client.RecognizeAsync(TextImages.Render(""))).ReadJsonAsync();

        Assert.Null(body["episodeId"]);
        Assert.Empty(body["lines"]!.AsArray());
        Assert.Empty(await (await client.GetAsync("/episodes")).ReadJsonArrayAsync());
    }
}
