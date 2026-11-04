namespace EasyLangApi.Recognition;

/// <summary>Распознавание невозможно в этой установке: нет Tesseract, он завис и т.п. → 503.</summary>
public sealed class TextRecognitionUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Tesseract запустился, но не смог обработать именно это изображение → 422.</summary>
public sealed class TextRecognitionFailedException(string message) : Exception(message);
