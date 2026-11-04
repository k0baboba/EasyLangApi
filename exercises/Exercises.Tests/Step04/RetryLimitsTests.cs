using System.Net;
using EasyLangApi.Explaining;
using Exercises.Infrastructure;

namespace Exercises.Step04;

/// <summary>Шаг 04. Всего не больше трёх попыток, а то, что повтор не исправит (404, 400), не повторяется.</summary>
public class RetryLimitsTests
{
    [Fact]
    public async Task Always_503_means_three_attempts_then_unavailable()
    {
        var handler = new ConcurrencyHandler(_ => (TimeSpan.Zero, ConcurrencyHandler.Status(HttpStatusCode.ServiceUnavailable)));

        await Assert.ThrowsAsync<ExplainerUnavailableException>(
            () => Step03.RetryTests.Create(handler).ExplainAsync("beat", PhraseContext.Empty));
        Assert.Equal(3, handler.Paths.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Client_errors_are_not_retried(HttpStatusCode status)
    {
        var handler = new ConcurrencyHandler(_ => (TimeSpan.Zero, ConcurrencyHandler.Status(status)));

        try
        {
            await Step03.RetryTests.Create(handler).ExplainAsync("beat", PhraseContext.Empty);
        }
        catch (ExplainerUnavailableException)
        {
            // 400 — ошибка, но повторять её бессмысленно
        }

        Assert.Single(handler.Paths);
    }

    [Fact]
    public async Task Retries_keep_working_for_every_request()
    {
        // Три слова, у каждого первый ответ 502. Повтор должен сработать для каждого запроса.
        var seen = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        var handler = new ConcurrencyHandler(request =>
        {
            var term = ConcurrencyHandler.Term(request);
            if (term.Contains(' '))
                return (TimeSpan.Zero, ConcurrencyHandler.Status(HttpStatusCode.NotFound));
            return seen.AddOrUpdate(term, 1, (_, n) => n + 1) == 1
                ? (TimeSpan.Zero, ConcurrencyHandler.Status(HttpStatusCode.BadGateway))
                : (TimeSpan.Zero, ConcurrencyHandler.Entry(term));
        });

        var explanation = await Step03.RetryTests.Create(handler).ExplainAsync("rain window coffee", PhraseContext.Empty);

        Assert.Equal(3, explanation.Senses.Count);
    }
}
