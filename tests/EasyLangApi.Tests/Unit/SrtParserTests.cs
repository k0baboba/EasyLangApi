using EasyLangApi.Episodes;

namespace EasyLangApi.Tests.Unit;

public class SrtParserTests
{
    [Fact]
    public void Parses_timings_and_joins_multiline_text()
    {
        const string srt = """
            1
            00:00:01,600 --> 00:00:04,200
            Hello there,
            general.

            2
            01:02:03,004 --> 01:02:05,000
            Second line
            """;

        var lines = SrtParser.Parse(srt);

        Assert.Equal(2, lines.Count);
        Assert.Equal(new SourceLine(1600, 4200, "Hello there, general."), lines[0]);
        Assert.Equal(((1 * 60 + 2) * 60 + 3) * 1000 + 4, lines[1].StartMs);
    }

    [Fact]
    public void Handles_bom_crlf_and_formatting_tags()
    {
        var srt = "﻿1\r\n00:00:01,000 --> 00:00:02,000\r\n<i>Quiet</i> {\\an8}please\r\n\r\n";

        var line = Assert.Single(SrtParser.Parse(srt));

        Assert.Equal("Quiet please", line.Text);
    }

    [Fact]
    public void Skips_broken_blocks_but_keeps_the_rest()
    {
        const string srt = """
            1
            not a timing line
            Lost text

            2
            00:00:05,000 --> 00:00:06,000

            3
            00:00:07.5 --> 00:00:08.25
            Dot separator and short millis
            """;

        var line = Assert.Single(SrtParser.Parse(srt));

        Assert.Equal(7500, line.StartMs);
        Assert.Equal(8250, line.EndMs);
    }

    [Fact]
    public void Returns_empty_list_for_non_srt_text() =>
        Assert.Empty(SrtParser.Parse("just some text\nwithout timings"));

    [Fact]
    public void Sample_file_in_repository_parses()
    {
        var content = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sample.srt"));

        var lines = SrtParser.Parse(content);

        Assert.Equal(12, lines.Count);
        Assert.All(lines, l => Assert.True(l.EndMs > l.StartMs));
    }
}
