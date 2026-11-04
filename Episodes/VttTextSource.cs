namespace EasyLangApi.Episodes;

public sealed class VttTextSource : ISubtitleSource
{
    public string Extension => ".vtt";

    public string FormatName => "WebVTT";

    public async Task<IReadOnlyList<SourceLine>> ReadLinesAsync(Stream content, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(content, detectEncodingFromByteOrderMarks: true);
        return VttParser.Parse(await reader.ReadToEndAsync(cancellationToken));
    }
}
