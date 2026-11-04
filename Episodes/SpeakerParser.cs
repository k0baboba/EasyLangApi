using System.Text.RegularExpressions;

namespace EasyLangApi.Episodes;

/// <summary>Субтитры часто подписывают говорящего: "MAYA: Coffee's on the house."</summary>
public static partial class SpeakerParser
{
    // Имя заглавными (допустимы пробел, точка, дефис, апостроф), затем двоеточие.
    [GeneratedRegex(@"^\s*-?\s*([A-Z][A-Z0-9 .'\-]{0,30}):\s")]
    private static partial Regex SpeakerRegex();

    public static string? TryExtract(string lineText)
    {
        var match = SpeakerRegex().Match(lineText);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
}
