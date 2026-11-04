using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyLangClient;

public sealed record TokenPair(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt);

public sealed record RecognizedLine(int Id, int Index, string Text);

public sealed record RecognitionResult(string Text, int? EpisodeId, List<RecognizedLine> Lines);

public sealed record Sense(string Word, string? PartOfSpeech, string Definition, string? Example);

public sealed record Explanation(
    string Phrase, string Source, bool Found, string? Summary, string? Phonetic,
    string? Translation, string? ContextNote, List<Sense> Senses);

public sealed record LookupResult(int LookupId, int KnowledgeItemId, bool IsNewItem, string Phrase, Explanation Explanation);

/// <summary>Ошибка, которую можно показать пользователю как есть.</summary>
public sealed class ApiException(string message) : Exception(message);

/// <summary>
/// Тонкая обёртка над HTTP API сервера. Клиент ничего не разбирает сам:
/// отправляет картинку и фразу, показывает ответ.
/// </summary>
public sealed class ApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private TokenPair? _tokens;

    public bool IsLoggedIn => _tokens is not null;

    public Task LoginAsync(string email, string password) => AuthAsync("/auth/login", email, password);

    public Task RegisterAsync(string email, string password) => AuthAsync("/auth/register", email, password);

    public async Task<RecognitionResult> RecognizeAsync(byte[] png, string? title, int? episodeId)
    {
        var response = await SendAuthorizedAsync(() =>
        {
            var form = new MultipartFormDataContent();
            var image = new ByteArrayContent(png);
            image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(image, "image", "capture.png");
            if (!string.IsNullOrWhiteSpace(title))
                form.Add(new StringContent(title), "title");
            if (episodeId is not null)
                form.Add(new StringContent(episodeId.Value.ToString()), "episodeId");
            return new HttpRequestMessage(HttpMethod.Post, "/recognize") { Content = form };
        });
        return await ReadAsync<RecognitionResult>(response);
    }

    public async Task<LookupResult> ExplainAsync(int lineId, string phrase)
    {
        var url = $"/lines/{lineId}/explain?phrase={Uri.EscapeDataString(phrase)}";
        var response = await SendAuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Get, url));
        return await ReadAsync<LookupResult>(response);
    }

    private async Task AuthAsync(string path, string email, string password)
    {
        var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { email, password }),
        });
        _tokens = await ReadAsync<TokenPair>(response);
    }

    /// <summary>Отправка с токеном. На 401 один раз пробуем обновить токены и повторить.</summary>
    private async Task<HttpResponseMessage> SendAuthorizedAsync(Func<HttpRequestMessage> createRequest)
    {
        if (_tokens is null)
            throw new ApiException("Сначала войдите.");

        var response = await SendAsync(() => WithToken(createRequest()));
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        response.Dispose();
        if (!await TryRefreshAsync())
        {
            _tokens = null;
            throw new ApiException("Сессия истекла, войдите заново.");
        }

        return await SendAsync(() => WithToken(createRequest()));
    }

    private async Task<bool> TryRefreshAsync()
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, "/auth/refresh")
        {
            Content = JsonContent.Create(new { refreshToken = _tokens!.RefreshToken }),
        });
        if (!response.IsSuccessStatusCode)
            return false;
        _tokens = await response.Content.ReadFromJsonAsync<TokenPair>(Json);
        return _tokens is not null;
    }

    private HttpRequestMessage WithToken(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokens!.AccessToken);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> createRequest)
    {
        using var request = createRequest();
        try
        {
            return await http.SendAsync(request);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException($"Сервер {http.BaseAddress} недоступен: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            throw new ApiException("Сервер не ответил вовремя.");
        }
    }

    /// <summary>Успех — тело как T; ошибка — текст из ProblemDetails (title/detail).</summary>
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<T>(Json)
                       ?? throw new ApiException("Сервер вернул пустой ответ.");

            var text = await response.Content.ReadAsStringAsync();
            try
            {
                var problem = JsonSerializer.Deserialize<Problem>(text, Json);
                var message = problem?.Detail ?? problem?.Title;
                if (!string.IsNullOrWhiteSpace(message))
                    throw new ApiException(message);
            }
            catch (JsonException)
            {
                // не ProblemDetails — ниже общий текст
            }

            throw new ApiException($"Сервер ответил {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
    }

    private sealed record Problem(string? Title, string? Detail);
}
