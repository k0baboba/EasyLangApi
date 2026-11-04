using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Exercises.Infrastructure;

/// <summary>
/// Подставной HTTP, который считает, сколько запросов выполняется одновременно.
/// Безопасен для параллельных вызовов (в отличие от StubHttpHandler из основных тестов).
/// </summary>
public sealed class ConcurrencyHandler(Func<HttpRequestMessage, (TimeSpan Delay, HttpResponseMessage Response)> respond)
    : HttpMessageHandler
{
    private int _inFlight;
    private int _maxInFlight;

    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    public ConcurrentQueue<string> Paths { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Paths.Enqueue(Uri.UnescapeDataString(request.RequestUri!.AbsolutePath));
        var now = Interlocked.Increment(ref _inFlight);
        int max;
        while (now > (max = Volatile.Read(ref _maxInFlight)) && Interlocked.CompareExchange(ref _maxInFlight, now, max) != max)
        {
        }

        try
        {
            var (delay, response) = respond(request);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            return response;
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    /// <summary>Последний сегмент пути — слово, которое спросили у словаря.</summary>
    public static string Term(HttpRequestMessage request) =>
        Uri.UnescapeDataString(request.RequestUri!.Segments[^1]);

    /// <summary>Ответ в формате dictionaryapi.dev с одним значением "meaning of {word}".</summary>
    public static HttpResponseMessage Entry(string word) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""[{"word":"{{word}}","meanings":[{"partOfSpeech":"noun","definitions":[{"definition":"meaning of {{word}}"}]}]}]""",
                Encoding.UTF8, "application/json"),
        };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status) { Content = new StringContent("") };
}
