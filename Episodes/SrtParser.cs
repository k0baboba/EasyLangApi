using System.Text.RegularExpressions;

namespace EasyLangApi.Episodes;

/// <summary>
/// Разбор SubRip (.srt). Формат: блоки через пустую строку, в блоке номер,
/// строка таймингов "00:00:01,600 --> 00:00:04,200" и одна или несколько строк текста.
/// Битые блоки пропускаются, а не роняют весь файл.
/// </summary>
public static partial class SrtParser
{
    [GeneratedRegex(@"(\d{1,2}):(\d{2}):(\d{2})[,.](\d{1,3})\s*-->\s*(\d{1,2}):(\d{2}):(\d{2})[,.](\d{1,3})")]
    private static partial Regex TimingRegex();

    // <i>курсив</i>, <font color=...> и ASS-теги вида {\an8}
    [GeneratedRegex(@"<[^>]*>|\{\\[^}]*\}")]
    private static partial Regex FormattingTagRegex();

    [GeneratedRegex(@"\n[ \t]*\n")]
    private static partial Regex BlankLineRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public static IReadOnlyList<SourceLine> Parse(string content)
    {
        var normalized = content.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n');
        var result = new List<SourceLine>();

        foreach (var block in BlankLineRegex().Split(normalized))
        {
            var rows = block.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var timingRow = Array.FindIndex(rows, r => TimingRegex().IsMatch(r));
            if (timingRow < 0)
                continue;

            var match = TimingRegex().Match(rows[timingRow]);
            var text = string.Join(' ', rows.Skip(timingRow + 1));
            text = FormattingTagRegex().Replace(text, "");
            text = WhitespaceRegex().Replace(text, " ").Trim();
            if (text.Length == 0)
                continue;

            var start = ToMilliseconds(match, 1);
            var end = ToMilliseconds(match, 5);
            result.Add(new SourceLine(start, Math.Max(start, end), text));
        }

        return result;
    }

    private static int ToMilliseconds(Match match, int firstGroup)
    {
        var hours = int.Parse(match.Groups[firstGroup].Value);
        var minutes = int.Parse(match.Groups[firstGroup + 1].Value);
        var seconds = int.Parse(match.Groups[firstGroup + 2].Value);
        // "5" после запятой — это 500 мс, а не 5: дробная часть секунды.
        var millis = int.Parse(match.Groups[firstGroup + 3].Value.PadRight(3, '0'));
        return ((hours * 60 + minutes) * 60 + seconds) * 1000 + millis;
    }
}
