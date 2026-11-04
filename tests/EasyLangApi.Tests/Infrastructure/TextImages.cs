using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EasyLangApi.Tests.Infrastructure;

/// <summary>Рисует PNG с текстом: тёмный текст на светлом фоне, крупный шрифт из Assets.</summary>
public static class TextImages
{
    private static readonly Lazy<FontFamily> Family = new(() =>
        new FontCollection().Add(Path.Combine(AppContext.BaseDirectory, "Assets", "DejaVuSans.ttf")));

    /// <param name="paragraphs">Каждый абзац рисуется отдельным блоком с отступом.</param>
    public static byte[] Render(params string[] paragraphs)
    {
        var font = Family.Value.CreateFont(40);
        const int lineHeight = 56;
        const int paragraphGap = 50;
        var lineCount = paragraphs.Sum(p => p.Split('\n').Length);
        var height = 60 + lineCount * lineHeight + paragraphs.Length * paragraphGap;

        using var image = new Image<Rgba32>(1400, height, Color.White);
        image.Mutate(ctx =>
        {
            var y = 40f;
            foreach (var paragraph in paragraphs)
            {
                foreach (var row in paragraph.Split('\n'))
                {
                    ctx.DrawText(row, font, Color.Black, new PointF(40, y));
                    y += lineHeight;
                }

                y += paragraphGap;
            }
        });

        using var output = new MemoryStream();
        image.SaveAsPng(output);
        return output.ToArray();
    }
}
