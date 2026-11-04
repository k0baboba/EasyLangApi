using System.Text.RegularExpressions;
using EasyLangApi.Episodes;

namespace EasyLangApi.Recognition;

/// <summary>
/// Скриншот как источник реплик. Tesseract разделяет абзацы пустой строкой;
/// абзац — это обычно одна реплика в окне диалога, перенесённая на несколько строк.
/// Таймингов у скриншота нет — StartMs и EndMs равны нулю.
/// </summary>
public sealed partial class ImageTextSource(ITextRecognizer recognizer) : ITextSource
{
    [GeneratedRegex(@"\n[ \t]*\n")]
    private static partial Regex ParagraphBreakRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public async Task<IReadOnlyList<SourceLine>> ReadLinesAsync(Stream content, CancellationToken cancellationToken)
    {
        var text = await recognizer.RecognizeAsync(content, cancellationToken);
        return SplitParagraphs(text);
    }

    public static IReadOnlyList<SourceLine> SplitParagraphs(string text) =>
        ParagraphBreakRegex()
            .Split(text.Replace("\r\n", "\n"))
            .Select(p => WhitespaceRegex().Replace(p, " ").Trim())
            .Where(p => p.Length > 0)
            .Select(p => new SourceLine(0, 0, p))
            .ToList();
}
