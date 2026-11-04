namespace EasyLangApi.Recognition;

public interface ITextRecognizer
{
    /// <summary>Распознаёт текст на изображении. Абзацы разделены пустой строкой.</summary>
    Task<string> RecognizeAsync(Stream image, CancellationToken cancellationToken = default);
}
