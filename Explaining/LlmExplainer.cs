using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace EasyLangApi.Explaining;

/// <summary>
/// Разбор через языковую модель по протоколу OpenAI Chat Completions
/// (POST {BaseUrl}/chat/completions). Подходит для OpenAI, OpenRouter, Ollama и т.п.
/// </summary>
public sealed class LlmExplainer(HttpClient http, IOptions<LlmOptions> options) : IPhraseExplainer
{
    public const string SourceName = "llm";

    public async Task<Explanation> ExplainAsync(
        string phrase, PhraseContext context, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var body = new ChatRequest(
            settings.Model,
            [
                new ChatMessage("system", PromptBuilder.BuildSystemPrompt(settings.ExplanationLanguage)),
                new ChatMessage("user", PromptBuilder.BuildUserPrompt(phrase, context)),
            ],
            Temperature: 0.2,
            ResponseFormat: settings.UseJsonMode ? new ResponseFormat("json_object") : null);

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ExplainerUnavailableException("Языковая модель недоступна: не удалось соединиться.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ExplainerUnavailableException("Языковая модель не ответила вовремя.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new ExplainerUnavailableException($"Языковая модель ответила кодом {(int)response.StatusCode}.");

            ChatResponse? chat;
            try
            {
                chat = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new ExplainerUnavailableException("Языковая модель вернула ответ в неожиданном формате.", ex);
            }

            var content = chat?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
                throw new ExplainerUnavailableException("Языковая модель вернула пустой ответ.");

            return LlmResponseParser.Parse(phrase, content);
        }
    }

    // Протокол OpenAI использует snake_case, поэтому имена заданы явно.
    internal sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("response_format"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ResponseFormat? ResponseFormat);

    internal sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    internal sealed record ResponseFormat([property: JsonPropertyName("type")] string Type);

    internal sealed record ChatResponse([property: JsonPropertyName("choices")] List<ChatChoice>? Choices);

    internal sealed record ChatChoice([property: JsonPropertyName("message")] ChatMessage? Message);
}
