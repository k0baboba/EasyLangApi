namespace EasyLangApi.Explaining;

public sealed class DictionaryOptions
{
    public const string SectionName = "Dictionary";

    public string BaseUrl { get; set; } = "https://api.dictionaryapi.dev/api/v2/entries/en/";
    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>
/// Любой API, совместимый с OpenAI Chat Completions: сам OpenAI, OpenRouter, локальный Ollama
/// (BaseUrl = http://localhost:11434/v1). Языковая модель включается, только если задан ApiKey.
/// Ollama ключ не проверяет, ему подойдёт любая непустая строка.
/// </summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string Model { get; set; } = "gpt-4o-mini";
    public string? ApiKey { get; set; }
    public string ExplanationLanguage { get; set; } = "Russian";
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Просить у API строго JSON (response_format). Выключите, если сервис это поле не понимает.</summary>
    public bool UseJsonMode { get; set; } = true;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(ApiKey);
}
