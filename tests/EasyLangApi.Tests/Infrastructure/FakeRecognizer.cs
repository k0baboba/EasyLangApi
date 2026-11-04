using EasyLangApi.Recognition;

namespace EasyLangApi.Tests.Infrastructure;

/// <summary>Распознаватель без Tesseract: возвращает заданный текст.</summary>
public sealed class FakeRecognizer(string text) : ITextRecognizer
{
    public int Calls { get; private set; }

    public Task<string> RecognizeAsync(Stream image, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(text);
    }
}
