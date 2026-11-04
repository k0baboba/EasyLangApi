using System.ComponentModel;
using System.Diagnostics;

namespace EasyLangApi.Tests.Infrastructure;

/// <summary>Есть ли tesseract в PATH. Тесты настоящего распознавания без него пропускаются.</summary>
public static class Tesseract
{
    private static readonly Lazy<bool> Available = new(() =>
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("tesseract", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            process!.WaitForExit(10_000);
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    });

    public static void SkipIfMissing()
    {
        if (!Available.Value)
            Assert.Skip("tesseract не установлен");
    }
}
