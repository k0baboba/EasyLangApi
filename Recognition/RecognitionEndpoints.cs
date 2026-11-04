using EasyLangApi.Episodes;
using EasyLangApi.Quotas;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace EasyLangApi.Recognition;

public static class RecognitionEndpoints
{
    public const long MaxImageBytes = 10 * 1024 * 1024;

    public sealed record RecognizedLine(int Id, int Index, string Text);

    public sealed record RecognitionResult(string Text, int? EpisodeId, IReadOnlyList<RecognizedLine> Lines);

    public static IEndpointRouteBuilder MapRecognitionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/recognize", RecognizeAsync)
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimitingExtensions.ExpensivePolicy);
        return app;
    }

    /// <summary>
    /// Форма: image — картинка; title — название игры/фильма (необязательно);
    /// episodeId — дописать реплики в существующий эпизод (необязательно).
    /// Распознанные абзацы сохраняются как реплики, их id можно сразу отдать в /lines/{id}/explain.
    /// </summary>
    private static async Task<Results<Ok<RecognitionResult>, NotFound, ProblemHttpResult>> RecognizeAsync(
        IFormFile? image,
        [FromForm] string? title,
        [FromForm] int? episodeId,
        ImageTextSource source,
        EpisodeImporter importer,
        CancellationToken cancellationToken)
    {
        if (image is null || image.Length == 0)
            return TypedResults.Problem("Нужно изображение в поле формы 'image'.", statusCode: 400);
        if (image.Length > MaxImageBytes)
            return TypedResults.Problem($"Изображение больше {MaxImageBytes / 1024 / 1024} МБ.", statusCode: 400);

        await using var stream = image.OpenReadStream();
        var header = new byte[8];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        if (ImageFormat.Detect(header.AsSpan(0, read)) is null)
            return TypedResults.Problem("Поддерживаются PNG, JPEG, BMP, GIF, TIFF и WebP.", statusCode: 400);
        stream.Position = 0;

        // Ошибки Tesseract (нет программы, не смог прочитать) превращает в 503/422 KnownExceptionHandler.
        var lines = await source.ReadLinesAsync(stream, cancellationToken);
        var text = string.Join("\n\n", lines.Select(l => l.Text));
        if (lines.Count == 0)
            return TypedResults.Ok(new RecognitionResult(text, episodeId, []));

        List<EasyLangApi.Data.Line> saved;
        if (episodeId is { } existingId)
        {
            var appended = await importer.AppendAsync(existingId, lines, cancellationToken);
            if (appended is null)
                return TypedResults.NotFound();
            saved = appended;
        }
        else
        {
            var episode = await importer.ImportAsync(title?.Trim() ?? "", image.FileName, lines, cancellationToken);
            saved = episode.Lines;
        }

        return TypedResults.Ok(new RecognitionResult(
            text,
            saved[0].EpisodeId,
            saved.Select(l => new RecognizedLine(l.Id, l.Index, l.Text)).ToList()));
    }
}
