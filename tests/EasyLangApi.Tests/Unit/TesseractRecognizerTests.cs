using EasyLangApi.Recognition;
using EasyLangApi.Tests.Infrastructure;
using EasyLangApi.Vocabulary;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyLangApi.Tests.Unit;

public class TesseractRecognizerTests
{
    private static TesseractRecognizer Create(OcrOptions? options = null) =>
        new(Options.Create(options ?? new OcrOptions()), NullLogger<TesseractRecognizer>.Instance);

    [Fact]
    public async Task Recognizes_text_on_generated_image()
    {
        Tesseract.SkipIfMissing();
        var image = TextImages.Render("Are you gonna spill the beans?");

        var text = await Create().RecognizeAsync(new MemoryStream(image));

        Assert.Contains("spill the beans", PhraseNormalizer.Normalize(text));
    }

    [Fact]
    public async Task Keeps_paragraphs_apart()
    {
        Tesseract.SkipIfMissing();
        var image = TextImages.Render("Coffee is on the house tonight.", "Hit the road, you need some rest.");

        var lines = ImageTextSource.SplitParagraphs(await Create().RecognizeAsync(new MemoryStream(image)));

        Assert.Equal(2, lines.Count);
        Assert.Contains("on the house", PhraseNormalizer.Normalize(lines[0].Text));
        Assert.Contains("hit the road", PhraseNormalizer.Normalize(lines[1].Text));
    }

    [Fact]
    public async Task Missing_tesseract_is_reported_clearly()
    {
        var recognizer = Create(new OcrOptions { TesseractPath = "/nonexistent/tesseract" });

        var ex = await Assert.ThrowsAsync<TextRecognitionUnavailableException>(
            () => recognizer.RecognizeAsync(new MemoryStream(TextImages.Render("x"))));

        Assert.Contains("Tesseract не найден", ex.Message);
    }

    [Fact]
    public async Task Broken_image_is_a_recognition_failure()
    {
        Tesseract.SkipIfMissing();
        var broken = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };

        await Assert.ThrowsAsync<TextRecognitionFailedException>(() => Create().RecognizeAsync(new MemoryStream(broken)));
    }

    [Fact]
    public async Task Missing_language_data_is_unavailable()
    {
        Tesseract.SkipIfMissing();
        var recognizer = Create(new OcrOptions { Language = "zzz" });

        await Assert.ThrowsAsync<TextRecognitionUnavailableException>(
            () => recognizer.RecognizeAsync(new MemoryStream(TextImages.Render("hello"))));
    }
}
