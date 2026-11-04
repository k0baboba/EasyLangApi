namespace EasyLangApi.Episodes;

public sealed class SrtTextSource : ISubtitleSource
{
    public string Extension => ".srt";

    public string FormatName => "SRT";

    public async Task<IReadOnlyList<SourceLine>> ReadLinesAsync(Stream content, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(content, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return SrtParser.Parse(text);
    }
}
