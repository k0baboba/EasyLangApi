using System.Text.RegularExpressions;

namespace EasyLangApi.Vocabulary;

/// <summary>
/// Приводит фразу к виду, по которому KnowledgeItem ищется в базе:
/// нижний регистр, одинаковые апострофы, без пунктуации, слова через один пробел.
/// "Don’t  push it!" → "don't push it".
/// </summary>
public static partial class PhraseNormalizer
{
    // Слово — буквы/цифры, внутри могут быть апострофы: don't, rock'n'roll.
    [GeneratedRegex(@"[\p{L}\p{N}]+(?:'[\p{L}\p{N}]+)*")]
    private static partial Regex WordRegex();

    public static IReadOnlyList<string> Tokenize(string text)
    {
        var unified = text.ToLowerInvariant().Replace('’', '\'').Replace('‘', '\'');
        return WordRegex().Matches(unified).Select(m => m.Value).ToList();
    }

    public static string Normalize(string phrase) => string.Join(' ', Tokenize(phrase));

    /// <summary>Есть ли в тексте фраза целиком, как непрерывная последовательность слов.</summary>
    public static bool ContainsPhrase(IReadOnlyList<string> textTokens, IReadOnlyList<string> phraseTokens)
    {
        if (phraseTokens.Count == 0 || phraseTokens.Count > textTokens.Count)
            return false;

        for (var start = 0; start <= textTokens.Count - phraseTokens.Count; start++)
        {
            var matches = true;
            for (var i = 0; i < phraseTokens.Count && matches; i++)
                matches = textTokens[start + i] == phraseTokens[i];
            if (matches)
                return true;
        }

        return false;
    }

    public static bool ContainsPhrase(string text, string normalizedPhrase) =>
        ContainsPhrase(Tokenize(text), normalizedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
