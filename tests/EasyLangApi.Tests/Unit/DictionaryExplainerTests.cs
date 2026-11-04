using System.Net;
using EasyLangApi.Explaining;
using EasyLangApi.Tests.Infrastructure;

namespace EasyLangApi.Tests.Unit;

public class DictionaryExplainerTests
{
    private const string BeatJson = """
        [{"word":"beat","phonetic":"/biːt/","meanings":[
          {"partOfSpeech":"verb","definitions":[{"definition":"To hit or strike repeatedly.","example":"He beat the drum."}]},
          {"partOfSpeech":"adjective","definitions":[{"definition":"Exhausted after hard work.","example":"I'm beat."}]}
        ]}]
        """;

    private static (DictionaryExplainer Explainer, StubHttpHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHttpHandler(respond);
        return (new DictionaryExplainer(handler.CreateClient("https://dict.test/entries/en/")), handler);
    }

    [Fact]
    public async Task Maps_dictionary_response()
    {
        var (explainer, handler) = Create(_ => StubHttpHandler.Json(BeatJson));

        var result = await explainer.ExplainAsync("beat", PhraseContext.Empty);

        Assert.Equal("https://dict.test/entries/en/beat", handler.Requests.Single().RequestUri!.ToString());
        Assert.True(result.Found);
        Assert.Equal("dictionary", result.Source);
        Assert.Equal("/biːt/", result.Phonetic);
        Assert.Equal(2, result.Senses.Count);
        Assert.Equal("To hit or strike repeatedly.", result.Summary);
    }

    [Fact]
    public async Task Puts_sense_matching_context_first()
    {
        var (explainer, _) = Create(_ => StubHttpHandler.Json(BeatJson));
        var context = new PhraseContext(CurrentLine: "I'm beat. So much hard work at the docks.");

        var result = await explainer.ExplainAsync("beat", context);

        Assert.Equal("adjective", result.Senses[0].PartOfSpeech);
        Assert.Equal("Exhausted after hard work.", result.Summary);
    }

    [Fact]
    public async Task Unknown_phrase_falls_back_to_separate_words_but_is_not_found()
    {
        var (explainer, handler) = Create(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/beat")
                ? StubHttpHandler.Json(BeatJson)
                : StubHttpHandler.Json("""{"title":"No Definitions Found"}""", HttpStatusCode.NotFound));

        var result = await explainer.ExplainAsync("dead beat", PhraseContext.Empty);

        Assert.False(result.Found);
        Assert.Null(result.Summary);
        Assert.All(result.Senses, s => Assert.Equal("beat", s.Word));
        // целиком, потом "dead", потом "beat"
        Assert.Equal(3, handler.Requests.Count);
        Assert.EndsWith("/dead%20beat", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Server_error_becomes_unavailable()
    {
        var (explainer, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<ExplainerUnavailableException>(() => explainer.ExplainAsync("beat", PhraseContext.Empty));
    }

    [Fact]
    public async Task Network_failure_becomes_unavailable()
    {
        var (explainer, _) = Create(_ => throw new HttpRequestException("no route"));

        var ex = await Assert.ThrowsAsync<ExplainerUnavailableException>(
            () => explainer.ExplainAsync("beat", PhraseContext.Empty));
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }
}
