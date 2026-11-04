namespace EasyLangApi.Explaining;

/// <summary>
/// Контекст, в котором встретилась фраза. Все поля необязательные:
/// сборщик промпта использует то, что пришло. CurrentLine добавлено к полям из SPEC.md —
/// это сама реплика, в которой стоит фраза.
/// </summary>
public sealed record PhraseContext(
    string? Title = null,
    string? Speaker = null,
    IReadOnlyList<string>? Before = null,
    string? CurrentLine = null,
    IReadOnlyList<string>? After = null)
{
    public static PhraseContext Empty { get; } = new();
}
