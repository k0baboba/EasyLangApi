namespace EasyLangApi.Episodes;

/// <summary>Источник реплик из файла субтитров. Расширение говорит, какой файл он понимает.</summary>
public interface ISubtitleSource : ITextSource
{
    /// <summary>Расширение с точкой, например ".srt".</summary>
    string Extension { get; }

    string FormatName { get; }
}
