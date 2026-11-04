using System.Text.Json;

namespace EasyLangApi.Explaining;

/// <summary>
/// Разбирает текст ответа модели. Модели не всегда слушаются формата: оборачивают JSON
/// в ```json ... ```, добавляют пояснения до и после. Берём первый объект { ... }.
/// Если JSON не разобрался — не теряем ответ, а кладём текст целиком в Summary.
/// </summary>
public static class LlmResponseParser
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static Explanation Parse(string phrase, string content)
    {
        var json = ExtractJsonObject(content);
        LlmAnswer? answer = null;
        if (json is not null)
        {
            try
            {
                answer = JsonSerializer.Deserialize<LlmAnswer>(json, Options);
            }
            catch (JsonException)
            {
                // Похоже на JSON, но не он — ниже вернём сырой текст.
            }
        }

        if (answer is null || string.IsNullOrWhiteSpace(answer.Meaning) && string.IsNullOrWhiteSpace(answer.Translation))
        {
            var text = content.Trim();
            return new Explanation
            {
                Phrase = phrase,
                Source = LlmExplainer.SourceName,
                Found = text.Length > 0,
                Summary = text.Length > 0 ? text : null,
            };
        }

        var meaning = answer.Meaning ?? answer.Translation!;
        return new Explanation
        {
            Phrase = phrase,
            Source = LlmExplainer.SourceName,
            Found = true,
            Summary = meaning,
            Translation = answer.Translation,
            ContextNote = string.IsNullOrWhiteSpace(answer.ContextNote) ? null : answer.ContextNote,
            Senses = [new Sense(phrase, answer.PartOfSpeech, meaning, answer.Example)],
        };
    }

    internal static string? ExtractJsonObject(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : null;
    }

    private sealed record LlmAnswer(
        string? Translation, string? Meaning, string? PartOfSpeech, string? ContextNote, string? Example);
}
