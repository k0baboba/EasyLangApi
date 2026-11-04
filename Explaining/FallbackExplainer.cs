namespace EasyLangApi.Explaining;

/// <summary>
/// Декоратор: сначала основной разборщик (языковая модель), при его недоступности — запасной (словарь).
/// Переход пишется в лог предупреждением с причиной, чтобы сломанный ключ не прятался за рабочим словарём.
/// Отмену запроса не перехватывает: OperationCanceledException — не ExplainerUnavailableException.
/// </summary>
public sealed class FallbackExplainer(IPhraseExplainer primary, IPhraseExplainer fallback, ILogger<FallbackExplainer> logger)
    : IPhraseExplainer
{
    public async Task<Explanation> ExplainAsync(
        string phrase, PhraseContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await primary.ExplainAsync(phrase, context, cancellationToken);
        }
        catch (ExplainerUnavailableException ex)
        {
            logger.LogWarning(ex, "Основной разборщик недоступен, разбираю '{Phrase}' запасным", phrase);
            return await fallback.ExplainAsync(phrase, context, cancellationToken);
        }
    }
}
