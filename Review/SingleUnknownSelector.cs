using EasyLangApi.Vocabulary;

namespace EasyLangApi.Review;

public sealed record CandidateLine(int LineId, string Text, string EpisodeTitle = "");

public sealed record SelectedLine(int LineId, string Text, string EpisodeTitle, IReadOnlyList<string> OtherUnknown)
{
    public bool IsSingleUnknown => OtherUnknown.Count == 0;
}

/// <summary>
/// Отбор фраз по принципу «i + 1»: лучше всего запоминается фраза, в которой незнакомо
/// ровно одно — то, что мы сейчас повторяем. Остальное в ней должно быть понятно.
/// «Незнакомое» — элементы словаря, ещё не выученные (интервал меньше Review:KnownAfterDays).
/// Слова, которые пользователь ни разу не смотрел, считаются знакомыми.
/// </summary>
public static class SingleUnknownSelector
{
    /// <summary>Все реплики, где есть target, с тем, какие ещё незнакомые элементы в них встречаются.</summary>
    public static IReadOnlyList<SelectedLine> Analyze(
        string targetPhrase, IEnumerable<CandidateLine> lines, IReadOnlyCollection<string> otherUnknownPhrases)
    {
        var target = Split(targetPhrase);
        var others = otherUnknownPhrases.Select(p => (Phrase: p, Tokens: Split(p))).ToList();

        var result = new List<SelectedLine>();
        foreach (var line in lines)
        {
            var tokens = PhraseNormalizer.Tokenize(line.Text);
            if (!PhraseNormalizer.ContainsPhrase(tokens, target))
                continue;

            var present = others
                .Where(o => PhraseNormalizer.ContainsPhrase(tokens, o.Tokens))
                .Select(o => o.Phrase)
                .ToList();
            result.Add(new SelectedLine(line.LineId, line.Text, line.EpisodeTitle, present));
        }

        return result;
    }

    /// <summary>Реплики, где незнакомый элемент ровно один — target.</summary>
    public static IReadOnlyList<SelectedLine> Select(
        string targetPhrase, IEnumerable<CandidateLine> lines, IReadOnlyCollection<string> otherUnknownPhrases) =>
        Analyze(targetPhrase, lines, otherUnknownPhrases).Where(l => l.IsSingleUnknown).ToList();

    private static string[] Split(string normalizedPhrase) =>
        normalizedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
