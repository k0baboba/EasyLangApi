using System.Text;
using EasyLangApi.Episodes;
using EasyLangApi.Recognition;
using EasyLangApi.Tests.Infrastructure;

namespace EasyLangApi.Tests.Unit;

public class TextSourceTests
{
    [Fact]
    public void Recognized_paragraphs_become_lines()
    {
        var lines = ImageTextSource.SplitParagraphs("Are you gonna\nspill the beans?\n\n  \nNo cap.\r\n\r\n");

        Assert.Equal(["Are you gonna spill the beans?", "No cap."], lines.Select(l => l.Text));
        Assert.All(lines, l => Assert.Equal(0, l.StartMs));
    }

    [Fact]
    public async Task Image_source_uses_recognizer()
    {
        var recognizer = new FakeRecognizer("Hello\n\nWorld");
        ITextSource source = new ImageTextSource(recognizer);

        var lines = await source.ReadLinesAsync(new MemoryStream([1, 2, 3]), CancellationToken.None);

        Assert.Equal(2, lines.Count);
        Assert.Equal(1, recognizer.Calls);
    }

    [Fact]
    public async Task Srt_source_reads_stream()
    {
        ITextSource source = new SrtTextSource();
        var bytes = Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\nHi\n");

        var line = Assert.Single(await source.ReadLinesAsync(new MemoryStream(bytes), CancellationToken.None));

        Assert.Equal("Hi", line.Text);
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "jpeg")]
    [InlineData(new byte[] { (byte)'B', (byte)'M', 0, 0 }, "bmp")]
    [InlineData(new byte[] { (byte)'h', (byte)'i' }, null)]
    public void Detects_image_format_by_signature(byte[] header, string? expected) =>
        Assert.Equal(expected, ImageFormat.Detect(header));
}
