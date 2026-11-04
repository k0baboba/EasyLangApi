using EasyLangApi.Explaining;

namespace EasyLangApi.Tests.Infrastructure;

/// <summary>Разборщик без сети: отвечает предсказуемо и запоминает, какой контекст ему дали.</summary>
public sealed class FakeExplainer : IPhraseExplainer
{
    public List<(string Phrase, PhraseContext Context)> Calls { get; } = [];

    public bool Unavailable { get; set; }

    public Task<Explanation> ExplainAsync(string phrase, PhraseContext context, CancellationToken cancellationToken = default)
    {
        if (Unavailable)
            throw new ExplainerUnavailableException("Подставной разборщик выключен.");

        Calls.Add((phrase, context));
        return Task.FromResult(new Explanation
        {
            Phrase = phrase,
            Source = "fake",
            Found = true,
            Summary = $"meaning of {phrase}",
            Senses = [new Sense(phrase, "noun", $"meaning of {phrase}", null)],
        });
    }
}
