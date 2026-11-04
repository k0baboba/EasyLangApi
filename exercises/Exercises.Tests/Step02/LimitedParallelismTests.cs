using System.Net;
using EasyLangApi.Explaining;
using Exercises.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Exercises.Step02;

/// <summary>Шаг 02. Не больше двух запросов к бесплатному словарю одновременно, и значения идут в порядке слов фразы.</summary>
public class LimitedParallelismTests
{
    private static readonly Dictionary<string, int> DelayMs = new()
    {
        ["rain"] = 400, ["window"] = 300, ["coffee"] = 200, ["shift"] = 100,
    };

    [Fact]
    public async Task At_most_two_requests_at_once_and_senses_keep_phrase_order()
    {
        var handler = new ConcurrencyHandler(request =>
        {
            var term = ConcurrencyHandler.Term(request);
            return term.Contains(' ')
                ? (TimeSpan.Zero, ConcurrencyHandler.Status(HttpStatusCode.NotFound))
                : (TimeSpan.FromMilliseconds(DelayMs[term]), ConcurrencyHandler.Entry(term));
        });
        var explainer = new DictionaryExplainer(
            new HttpClient(handler) { BaseAddress = new Uri("http://dictionary.test/") },
            NullLogger<DictionaryExplainer>.Instance);

        var explanation = await explainer.ExplainAsync("rain window coffee shift", PhraseContext.Empty);

        Assert.Equal(2, handler.MaxInFlight);
        // Первое слово отвечает дольше всех, но его значение всё равно первое.
        Assert.Equal(["rain", "window", "coffee", "shift"], explanation.Senses.Select(s => s.Word));
    }

    [Fact]
    public async Task Failure_of_one_word_is_still_ExplainerUnavailableException()
    {
        var handler = new ConcurrencyHandler(request =>
        {
            var term = ConcurrencyHandler.Term(request);
            if (term.Contains(' '))
                return (TimeSpan.Zero, ConcurrencyHandler.Status(HttpStatusCode.NotFound));
            return term == "coffee"
                ? (TimeSpan.FromMilliseconds(50), ConcurrencyHandler.Status(HttpStatusCode.ServiceUnavailable))
                : (TimeSpan.FromMilliseconds(DelayMs[term]), ConcurrencyHandler.Entry(term));
        });
        var explainer = new DictionaryExplainer(
            new HttpClient(handler) { BaseAddress = new Uri("http://dictionary.test/") },
            NullLogger<DictionaryExplainer>.Instance);

        await Assert.ThrowsAsync<ExplainerUnavailableException>(
            () => explainer.ExplainAsync("rain window coffee shift", PhraseContext.Empty));
        Assert.Equal(2, handler.MaxInFlight);
    }
}
