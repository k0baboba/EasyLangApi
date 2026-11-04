using System.Net;
using System.Text.Json;
using EasyLangApi.Vocabulary;

namespace EasyLangApi.Explaining;

/// <summary>
/// Разбор через бесплатный https://dictionaryapi.dev. Контекст словарь не понимает,
/// поэтому контекст используется только чтобы поставить первым значение,
/// которое делит слова со сценой.
/// </summary>
public sealed class DictionaryExplainer(HttpClient http) : IPhraseExplainer
{
    public const string SourceName = "dictionary";

    private const int MaxSenses = 6;
    private const int DefinitionsPerMeaning = 2;
    private const int MaxWordsInFallback = 4;

    public async Task<Explanation> ExplainAsync(
        string phrase, PhraseContext context, CancellationToken cancellationToken = default)
    {
        var entries = await FetchAsync(phrase, cancellationToken);
        var senses = entries is null ? [] : ToSenses(phrase, entries);
        var foundAsWhole = senses.Count > 0;

        // Выражения из нескольких слов словарь часто не знает. Тогда показываем
        // значения отдельных слов, но не называем это смыслом фразы (Found = false).
        if (!foundAsWhole)
        {
            var words = PhraseNormalizer.Tokenize(phrase).Distinct().ToList();
            if (words.Count > 1)
            {
                foreach (var word in words.Where(w => w.Length > 2).Take(MaxWordsInFallback))
                {
                    var wordEntries = await FetchAsync(word, cancellationToken);
                    if (wordEntries is not null)
                        senses.AddRange(ToSenses(word, wordEntries).Take(DefinitionsPerMeaning));
                }
            }
        }

        var ranked = RankByContext(senses, context).Take(MaxSenses).ToList();

        return new Explanation
        {
            Phrase = phrase,
            Source = SourceName,
            Found = foundAsWhole,
            Summary = foundAsWhole ? ranked[0].Definition : null,
            Phonetic = entries?.Select(e => e.Phonetic).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)),
            Senses = ranked,
        };
    }

    /// <summary>null — словарь не знает слова (404). Сетевые сбои превращаются в ExplainerUnavailableException.</summary>
    private async Task<List<DictionaryEntry>?> FetchAsync(string term, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(Uri.EscapeDataString(term), cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ExplainerUnavailableException("Словарь недоступен: не удалось соединиться.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Отмена не от клиента — значит, сработал таймаут HttpClient.
            throw new ExplainerUnavailableException("Словарь не ответил вовремя.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            if (!response.IsSuccessStatusCode)
                throw new ExplainerUnavailableException($"Словарь ответил кодом {(int)response.StatusCode}.");

            try
            {
                return await response.Content.ReadFromJsonAsync<List<DictionaryEntry>>(cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new ExplainerUnavailableException("Словарь вернул ответ в неожиданном формате.", ex);
            }
        }
    }

    private static List<Sense> ToSenses(string word, List<DictionaryEntry> entries)
    {
        var senses = new List<Sense>();
        foreach (var meaning in entries.SelectMany(e => e.Meanings ?? []))
        {
            var definitions = (meaning.Definitions ?? [])
                .Where(d => !string.IsNullOrWhiteSpace(d.Definition))
                .Take(DefinitionsPerMeaning);
            foreach (var definition in definitions)
                senses.Add(new Sense(word, meaning.PartOfSpeech, definition.Definition!, definition.Example));
        }

        return senses;
    }

    /// <summary>
    /// Значения, у которых больше общих слов со сценой, идут первыми.
    /// Короткие слова (the, and, you) не считаются. OrderByDescending стабилен,
    /// поэтому при равенстве сохраняется порядок словаря.
    /// </summary>
    internal static IEnumerable<Sense> RankByContext(IEnumerable<Sense> senses, PhraseContext context)
    {
        var contextText = string.Join(' ',
            new[] { context.Title, context.CurrentLine }
                .Concat(context.Before ?? [])
                .Concat(context.After ?? [])
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        var contextWords = PhraseNormalizer.Tokenize(contextText).Where(w => w.Length > 3).ToHashSet();
        if (contextWords.Count == 0)
            return senses;

        return senses.OrderByDescending(s =>
            PhraseNormalizer.Tokenize($"{s.Definition} {s.Example}").Distinct().Count(contextWords.Contains));
    }
}

// Формат ответа dictionaryapi.dev (только нужные поля).
internal sealed record DictionaryEntry(string? Word, string? Phonetic, List<DictionaryMeaning>? Meanings);

internal sealed record DictionaryMeaning(string? PartOfSpeech, List<DictionaryDefinition>? Definitions);

internal sealed record DictionaryDefinition(string? Definition, string? Example);
