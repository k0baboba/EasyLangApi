using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace EasyLangApi.Recognition;

/// <summary>
/// Распознавание через командную строку Tesseract: `tesseract image.png stdout -l eng`.
/// Обёртки над нативной библиотекой капризны к версиям и платформам, а CLI одинаково
/// ставится через apt, brew и установщик для Windows.
/// </summary>
public sealed class TesseractRecognizer(IOptions<OcrOptions> options, ILogger<TesseractRecognizer> logger) : ITextRecognizer
{
    public async Task<string> RecognizeAsync(Stream image, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var imagePath = Path.Combine(Path.GetTempPath(), $"easylang-ocr-{Guid.NewGuid():N}.img");
        try
        {
            await using (var file = File.Create(imagePath))
                await image.CopyToAsync(file, cancellationToken);

            return await RunTesseractAsync(settings, imagePath, cancellationToken);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    private async Task<string> RunTesseractAsync(OcrOptions settings, string imagePath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(settings.TesseractPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // ArgumentList сам экранирует пробелы в путях — не нужно склеивать строку аргументов.
        startInfo.ArgumentList.Add(imagePath);
        startInfo.ArgumentList.Add("stdout");
        startInfo.ArgumentList.Add("-l");
        startInfo.ArgumentList.Add(settings.Language);
        if (settings.PageSegmentationMode is { } psm)
        {
            startInfo.ArgumentList.Add("--psm");
            startInfo.ArgumentList.Add(psm.ToString());
        }

        // Так рекомендует документация Tesseract, если несколько распознаваний идут параллельно.
        if (settings.ThreadLimit is { } threads)
            startInfo.Environment["OMP_THREAD_LIMIT"] = threads.ToString();

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            // Так Process сообщает, что исполняемого файла нет.
            throw new TextRecognitionUnavailableException(
                $"Tesseract не найден ('{settings.TesseractPath}'). Установите tesseract-ocr " +
                "или укажите путь в настройке Ocr:TesseractPath.", ex);
        }

        // Читаем оба потока параллельно с ожиданием: иначе процесс может зависнуть,
        // заполнив буфер stderr, пока мы ждём stdout.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            if (cancellationToken.IsCancellationRequested)
                throw;
            throw new TextRecognitionUnavailableException($"Tesseract не уложился в {settings.TimeoutSeconds} с.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            logger.LogWarning("Tesseract завершился с кодом {ExitCode}: {Stderr}", process.ExitCode, stderr);
            if (stderr.Contains("Failed loading language", StringComparison.OrdinalIgnoreCase))
                throw new TextRecognitionUnavailableException(
                    $"У Tesseract нет языковых данных для '{settings.Language}'.");
            throw new TextRecognitionFailedException("Tesseract не смог обработать изображение.");
        }

        return stdout.Replace("\f", "").Trim();
    }
}
