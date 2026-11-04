using System.Text.Json;
using EasyLangApi.Data;
using EasyLangApi.Explaining;
using EasyLangApi.Lookups;
using EasyLangApi.Review;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EasyLangApi.Vocabulary;

public static class VocabularyEndpoints
{
    public sealed record LastSeen(string Phrase, string LineText, string EpisodeTitle, string? Summary, DateTime At);

    public sealed record VocabularyItem(
        int Id, string Phrase, DateTime FirstSeenAt, double IntervalDays, DateTime DueAt, int LookupCount, LastSeen? LastSeen);

    public static IEndpointRouteBuilder MapVocabularyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/vocabulary", ListAsync);
        app.MapGet("/vocabulary/{id:int}/examples", ExamplesAsync);
        return app;
    }

    private static async Task<List<VocabularyItem>> ListAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.KnowledgeItems
            .OrderByDescending(k => k.FirstSeenAt)
            .Select(k => new
            {
                Item = k,
                LookupCount = k.Lookups.Count,
                Last = k.Lookups
                    .OrderByDescending(l => l.CreatedAt)
                    .Select(l => new { l.Phrase, LineText = l.Line.Text, EpisodeTitle = l.Line.Episode.Title, l.ExplanationJson, l.CreatedAt })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new VocabularyItem(
                r.Item.Id,
                r.Item.PhraseNormalized,
                r.Item.FirstSeenAt,
                r.Item.IntervalDays,
                r.Item.DueAt,
                r.LookupCount,
                r.Last is null
                    ? null
                    : new LastSeen(r.Last.Phrase, r.Last.LineText, r.Last.EpisodeTitle, ReadSummary(r.Last.ExplanationJson), r.Last.CreatedAt)))
            .ToList();
    }

    /// <summary>Реплики с этим элементом; singleUnknownOnly=true — только те, где он единственный незнакомый.</summary>
    private static async Task<Results<Ok<IReadOnlyList<SelectedLine>>, NotFound>> ExamplesAsync(
        int id, bool? singleUnknownOnly, AppDbContext db, ExampleLineFinder finder, CancellationToken cancellationToken)
    {
        var item = await db.KnowledgeItems.FirstOrDefaultAsync(k => k.Id == id, cancellationToken);
        if (item is null)
            return TypedResults.NotFound();

        var lines = await finder.FindAsync(item, cancellationToken);
        if (singleUnknownOnly == true)
            lines = lines.Where(l => l.IsSingleUnknown).ToList();
        return TypedResults.Ok(lines);
    }

    private static string? ReadSummary(string explanationJson) =>
        JsonSerializer.Deserialize<Explanation>(explanationJson, LookupService.JsonOptions)?.Summary;
}
