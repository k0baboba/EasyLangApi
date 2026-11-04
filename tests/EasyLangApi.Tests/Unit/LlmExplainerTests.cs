using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using EasyLangApi.Explaining;
using EasyLangApi.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyLangApi.Tests.Unit;

public class LlmExplainerTests
{
    private static string ChatResponse(string content) =>
        JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content } } } });

    private static (LlmExplainer Explainer, StubHttpHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond, LlmOptions? options = null)
    {
        var settings = options ?? new LlmOptions { ApiKey = "test-key", Model = "test-model" };
        var handler = new StubHttpHandler(respond);
        var explainer = new LlmExplainer(
            handler.CreateClient("http://llm.test/v1/"), Options.Create(settings), NullLogger<LlmExplainer>.Instance);
        return (explainer, handler);
    }

    [Fact]
    public async Task Sends_openai_compatible_request_with_context()
    {
        var (explainer, handler) = Create(_ => StubHttpHandler.Json(ChatResponse("""{"translation":"вымотан","meaning":"Очень устал."}""")));
        var context = new PhraseContext(Title: "Night Shift", Before: ["You look tired."], CurrentLine: "I'm beat.");

        var result = await explainer.ExplainAsync("beat", context);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://llm.test/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("test-key", request.Headers.Authorization.Parameter);
        // Тело с длиной, а не чанками: простые совместимые серверы чанки не принимают.
        Assert.NotNull(request.Content!.Headers.ContentLength);

        var body = JsonNode.Parse(handler.RequestBodies.Single()!)!;
        Assert.Equal("test-model", body["model"]!.GetValue<string>());
        Assert.Equal("json_object", body["response_format"]!["type"]!.GetValue<string>());
        var messages = body["messages"]!.AsArray();
        Assert.Equal("system", messages[0]!["role"]!.GetValue<string>());
        var user = messages[1]!["content"]!.GetValue<string>();
        Assert.Contains("Work: Night Shift", user);
        Assert.Contains("- You look tired.", user);
        Assert.Contains("Line with the phrase: I'm beat.", user);

        Assert.Equal("llm", result.Source);
        Assert.Equal("вымотан", result.Translation);
    }

    [Fact]
    public async Task Json_mode_can_be_turned_off_for_servers_without_it()
    {
        var options = new LlmOptions { ApiKey = "ollama", UseJsonMode = false };
        var (explainer, handler) = Create(_ => StubHttpHandler.Json(ChatResponse("tired")), options);

        await explainer.ExplainAsync("beat", PhraseContext.Empty);

        Assert.Null(JsonNode.Parse(handler.RequestBodies.Single()!)!["response_format"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Llm:ApiKey")]
    [InlineData(HttpStatusCode.NotFound, "Llm:Model")]
    [InlineData(HttpStatusCode.TooManyRequests, "лимит")]
    [InlineData(HttpStatusCode.BadGateway, "502")]
    public async Task Http_errors_become_unavailable_with_hint(HttpStatusCode status, string hint)
    {
        var (explainer, _) = Create(_ => StubHttpHandler.Json("""{"error":"nope"}""", status));

        var ex = await Assert.ThrowsAsync<ExplainerUnavailableException>(
            () => explainer.ExplainAsync("beat", PhraseContext.Empty));

        Assert.Contains(hint, ex.Message);
    }

    [Fact]
    public async Task Empty_completion_is_unavailable()
    {
        var (explainer, _) = Create(_ => StubHttpHandler.Json("""{"choices":[]}"""));

        await Assert.ThrowsAsync<ExplainerUnavailableException>(() => explainer.ExplainAsync("beat", PhraseContext.Empty));
    }

    [Fact]
    public async Task Non_json_response_is_unavailable()
    {
        var (explainer, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>") });

        await Assert.ThrowsAsync<ExplainerUnavailableException>(() => explainer.ExplainAsync("beat", PhraseContext.Empty));
    }

    [Fact]
    public async Task Connection_failure_is_unavailable()
    {
        var (explainer, _) = Create(_ => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<ExplainerUnavailableException>(() => explainer.ExplainAsync("beat", PhraseContext.Empty));
    }
}
