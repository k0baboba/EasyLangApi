using EasyLangApi.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyLangApi.Episodes;

public static class EpisodeEndpoints
{
    public const long MaxUploadBytes = 5 * 1024 * 1024;

    public sealed record EpisodeSummary(int Id, string Title, string SourceFile, DateTime CreatedAt, int LineCount);

    public sealed record LineDto(int Id, int Index, int StartMs, int EndMs, string Text);

    public static IEndpointRouteBuilder MapEpisodeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/episodes");

        group.MapPost("/", UploadAsync)
            // Форма приходит не из нашего HTML, а от клиента/скрипта, антифорджери-токена у него нет.
            .DisableAntiforgery();
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:int}/lines", GetLinesAsync);

        return app;
    }

    private static async Task<Results<Created<EpisodeSummary>, ProblemHttpResult>> UploadAsync(
        IFormFile? file,
        [FromForm] string? title,
        SrtTextSource source,
        EpisodeImporter importer,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return TypedResults.Problem("Нужен непустой файл .srt в поле формы 'file'.", statusCode: 400);
        if (file.Length > MaxUploadBytes)
            return TypedResults.Problem($"Файл больше {MaxUploadBytes / 1024 / 1024} МБ.", statusCode: 400);

        IReadOnlyList<SourceLine> lines;
        await using (var stream = file.OpenReadStream())
            lines = await source.ReadLinesAsync(stream, cancellationToken);

        if (lines.Count == 0)
            return TypedResults.Problem("В файле не найдено ни одной реплики в формате SRT.", statusCode: 400);

        var episodeTitle = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(file.FileName) : title.Trim();
        var episode = await importer.ImportAsync(episodeTitle, file.FileName, lines, cancellationToken);

        var summary = new EpisodeSummary(episode.Id, episode.Title, episode.SourceFile, episode.CreatedAt, episode.Lines.Count);
        return TypedResults.Created($"/episodes/{episode.Id}/lines", summary);
    }

    private static async Task<List<EpisodeSummary>> ListAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        return await db.Episodes
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new EpisodeSummary(e.Id, e.Title, e.SourceFile, e.CreatedAt, e.Lines.Count))
            .ToListAsync(cancellationToken);
    }

    private static async Task<Results<Ok<List<LineDto>>, NotFound>> GetLinesAsync(
        int id, AppDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Episodes.AnyAsync(e => e.Id == id, cancellationToken))
            return TypedResults.NotFound();

        var lines = await db.Lines
            .Where(l => l.EpisodeId == id)
            .OrderBy(l => l.Index)
            .Select(l => new LineDto(l.Id, l.Index, l.StartMs, l.EndMs, l.Text))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(lines);
    }
}
