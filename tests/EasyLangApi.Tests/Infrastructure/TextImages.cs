using SkiaSharp;

namespace EasyLangApi.Tests.Infrastructure;

/// <summary>Рисует PNG с текстом: тёмный текст на светлом фоне, крупный шрифт из Assets.</summary>
public static class TextImages
{
    private static readonly Lazy<SKTypeface> Typeface = new(() =>
        SKTypeface.FromFile(Path.Combine(AppContext.BaseDirectory, "Assets", "DejaVuSans.ttf"))
        ?? throw new InvalidOperationException("Не удалось загрузить шрифт Assets/DejaVuSans.ttf"));

    /// <param name="paragraphs">Каждый абзац рисуется отдельным блоком с отступом; '\n' — перенос строки.</param>
    public static byte[] Render(params string[] paragraphs)
    {
        using var font = new SKFont(Typeface.Value, 40);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        const int lineHeight = 56;
        const int paragraphGap = 50;
        var lineCount = paragraphs.Sum(p => p.Split('\n').Length);
        var height = 60 + lineCount * lineHeight + paragraphs.Length * paragraphGap;

        using var surface = SKSurface.Create(new SKImageInfo(1400, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        var y = 40f;
        foreach (var paragraph in paragraphs)
        {
            foreach (var row in paragraph.Split('\n'))
            {
                // В Skia y — это базовая линия текста, а не верхний край.
                canvas.DrawText(row, 40, y + font.Size, SKTextAlign.Left, font, paint);
                y += lineHeight;
            }

            y += paragraphGap;
        }

        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
