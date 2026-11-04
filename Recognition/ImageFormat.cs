namespace EasyLangApi.Recognition;

/// <summary>Проверка по первым байтам файла: это картинка, которую понимает Tesseract (Leptonica)?</summary>
public static class ImageFormat
{
    private static readonly (string Name, byte[] Magic)[] Signatures =
    [
        ("png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        ("jpeg", [0xFF, 0xD8, 0xFF]),
        ("bmp", "BM"u8.ToArray()),
        ("gif", "GIF8"u8.ToArray()),
        ("tiff", [0x49, 0x49, 0x2A, 0x00]),
        ("tiff", [0x4D, 0x4D, 0x00, 0x2A]),
        ("webp", "RIFF"u8.ToArray()),
    ];

    public static string? Detect(ReadOnlySpan<byte> header)
    {
        foreach (var (name, magic) in Signatures)
            if (header.StartsWith(magic))
                return name;
        return null;
    }
}
