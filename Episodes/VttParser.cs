using System.Text.RegularExpressions;

namespace EasyLangApi.Episodes;

/// <summary>
/// Разбор WebVTT (.vtt). Файл начинается с "WEBVTT", дальше блоки через пустую строку.
/// Реплика — блок со строкой таймингов "00:01.000 --> 00:04.000" (часы необязательны);
/// строки до неё — идентификатор, после неё — текст. Блоки NOTE и STYLE пропускаются.
/// </summary>
public static partial class VttParser
{
    [GeneratedRegex(@"^(?:(\d{1,2}):)?(\d{2}):(\d{2})\.(\d{3})\s+-->\s+(?:(\d{1,2}):)?(\d{2}):(\d{2})\.(\d{3})(?:\s|$)")]
    private static partial Regex TimingRegex();

    // <v Maya>, <i>, </v>, <c.yellow>, <00:00:01.000> — всё в угловых скобках.
    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\n[ \t]*\n")]
    private static partial Regex BlankLineRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^WEBVTT(?:[ \t].*)?$")]
    private static partial Regex HeaderRegex();

    public static IReadOnlyList<SourceLine> Parse(string content)
    {
        var normalized = content.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n');
        var blocks = BlankLineRegex().Split(normalized);
        if (blocks.Length == 0 || !HeaderRegex().IsMatch(blocks[0].Split('\n')[0].Trim()))
            return [];

        var result = new List<SourceLine>();
        foreach (var block in blocks.Skip(1))
        {
            var rows = block.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (rows.Length == 0 || rows[0].StartsWith("NOTE") || rows[0] == "STYLE" || rows[0] == "REGION")
                continue;

            var timingRow = Array.FindIndex(rows, r => TimingRegex().IsMatch(r));
            if (timingRow < 0)
                continue;

            var match = TimingRegex().Match(rows[timingRow]);
            var text = TagRegex().Replace(string.Join(' ', rows.Skip(timingRow + 1)), "");
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
        var hours = match.Groups[firstGroup].Success ? int.Parse(match.Groups[firstGroup].Value) : 0;
        var minutes = int.Parse(match.Groups[firstGroup + 1].Value);
        var seconds = int.Parse(match.Groups[firstGroup + 2].Value);
        var millis = int.Parse(match.Groups[firstGroup + 3].Value);
        return ((hours * 60 + minutes) * 60 + seconds) * 1000 + millis;
    }
}
