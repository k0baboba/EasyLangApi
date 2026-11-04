using System.Text;

namespace EasyLangApi.Explaining;

/// <summary>
/// Собирает промпт для языковой модели. Берёт из PhraseContext только то, что есть:
/// без названия — без строки "Work", без соседних реплик — без этих блоков,
/// без контекста вообще — просит самое частое значение.
/// </summary>
public static class PromptBuilder
{
    public static string BuildSystemPrompt(string explanationLanguage) => $$"""
        You help a {{explanationLanguage}}-speaking learner understand English from films, series and games.
        Explain the phrase as it is used in the given scene, not word by word:
        cover slang, idioms, register and tone when they matter.
        Reply with one JSON object and nothing else:
        {
          "translation": "natural translation of the phrase in this scene, in {{explanationLanguage}}",
          "meaning": "what the phrase means here, one or two sentences in {{explanationLanguage}}",
          "partOfSpeech": "part of speech in English, or idiom / phrasal verb / slang",
          "contextNote": "why it is said this way in this scene, in {{explanationLanguage}}, or null",
          "example": "one more short English example with the same meaning"
        }
        """;

    public static string BuildUserPrompt(string phrase, PhraseContext context)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine($"Phrase: \"{phrase}\"");

        var hasContext = false;
        if (!string.IsNullOrWhiteSpace(context.Title))
        {
            prompt.AppendLine($"Work: {context.Title}");
            hasContext = true;
        }

        if (!string.IsNullOrWhiteSpace(context.Speaker))
        {
            prompt.AppendLine($"Speaker: {context.Speaker}");
            hasContext = true;
        }

        hasContext |= AppendLines(prompt, "Previous lines:", context.Before);

        if (!string.IsNullOrWhiteSpace(context.CurrentLine))
        {
            prompt.AppendLine($"Line with the phrase: {context.CurrentLine}");
            hasContext = true;
        }

        hasContext |= AppendLines(prompt, "Next lines:", context.After);

        if (!hasContext)
            prompt.AppendLine("No context is available. Explain the most common meaning.");

        return prompt.ToString().TrimEnd();
    }

    private static bool AppendLines(StringBuilder prompt, string header, IReadOnlyList<string>? lines)
    {
        var nonEmpty = (lines ?? []).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (nonEmpty.Count == 0)
            return false;

        prompt.AppendLine(header);
        foreach (var line in nonEmpty)
            prompt.AppendLine($"- {line}");
        return true;
    }
}
