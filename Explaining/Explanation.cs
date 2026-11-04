namespace EasyLangApi.Explaining;

/// <summary>Результат разбора фразы. Сериализуется в Lookup.ExplanationJson.</summary>
public sealed record Explanation
{
    public required string Phrase { get; init; }

    /// <summary>Кто разобрал: "dictionary" или "llm".</summary>
    public required string Source { get; init; }

    /// <summary>false — источник ничего не знает о фразе.</summary>
    public bool Found { get; init; }

    /// <summary>Короткий смысл в одну строку.</summary>
    public string? Summary { get; init; }

    public string? Phonetic { get; init; }

    /// <summary>Перевод на язык пользователя (заполняет языковая модель).</summary>
    public string? Translation { get; init; }

    /// <summary>Почему фраза сказана именно так: регистр, сленг, отсылки (заполняет языковая модель).</summary>
    public string? ContextNote { get; init; }

    public IReadOnlyList<Sense> Senses { get; init; } = [];
}

/// <summary>Одно значение. Word нужен, когда выражение разобрано по отдельным словам.</summary>
public sealed record Sense(string Word, string? PartOfSpeech, string Definition, string? Example);
