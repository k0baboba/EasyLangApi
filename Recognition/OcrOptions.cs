namespace EasyLangApi.Recognition;

public sealed class OcrOptions
{
    public const string SectionName = "Ocr";

    /// <summary>Путь к исполняемому файлу tesseract. По умолчанию ищется в PATH.</summary>
    public string TesseractPath { get; set; } = "tesseract";

    /// <summary>Языки Tesseract через "+", например "eng" или "eng+rus".</summary>
    public string Language { get; set; } = "eng";

    /// <summary>
    /// Режим сегментации страницы (--psm). null — по умолчанию Tesseract (3, автоматически).
    /// Для одиночного блока диалога в игре часто лучше 6.
    /// </summary>
    public int? PageSegmentationMode { get; set; }

    public int TimeoutSeconds { get; set; } = 30;
}
