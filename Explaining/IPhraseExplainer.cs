namespace EasyLangApi.Explaining;

public interface IPhraseExplainer
{
    Task<Explanation> ExplainAsync(string phrase, PhraseContext context, CancellationToken cancellationToken = default);
}
