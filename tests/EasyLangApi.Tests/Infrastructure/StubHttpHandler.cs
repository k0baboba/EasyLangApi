using System.Net;
using System.Text;

namespace EasyLangApi.Tests.Infrastructure;

/// <summary>
/// Подставной HTTP: вместо сети вызывает функцию и запоминает запросы.
/// HttpClient принимает обработчик в конструкторе, так что код под тестом не меняется.
/// </summary>
public sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string?> RequestBodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
        return respond(request);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public HttpClient CreateClient(string baseAddress) => new(this) { BaseAddress = new Uri(baseAddress) };
}
