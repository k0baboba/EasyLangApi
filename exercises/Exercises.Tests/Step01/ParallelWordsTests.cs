using System.Net;
using EasyLangApi.Explaining;
using Exercises.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Exercises.Step01;

/// <summary>Шаг 01. Если словарь не знает выражения, запросы по отдельным словам идут одновременно.</summary>
public class ParallelWordsTests
{
    [Fact]
    public async Task Word_requests_overlap_in_time()
    {
        var handler = new ConcurrencyHandler(request =>
        {
            var term = ConcurrencyHandler.Term(request);
            return term.Contains(' ')
                ? (TimeSpan.Zero, ConcurrencyHandler.Status(HttpStatusCode.NotFound))
                : (TimeSpan.FromMilliseconds(300), ConcurrencyHandler.Entry(term));
        });
        var explainer = new DictionaryExplainer(
            new HttpClient(handler) { BaseAddress = new Uri("http://dictionary.test/") },
            NullLogger<DictionaryExplainer>.Instance);

        var explanation = await explainer.ExplainAsync("rain window coffee shift", PhraseContext.Empty);

        Assert.Equal(4, explanation.Senses.Count);
        Assert.True(handler.MaxInFlight >= 2,
            $"Запросы по словам шли по одному (одновременно не больше {handler.MaxInFlight}). Нужно, чтобы они перекрывались.");
    }
}
