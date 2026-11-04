using System.Text.Json;
using EasyLangApi.Data;
using EasyLangApi.Explaining;
using EasyLangApi.Lookups;
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

    private static string? ReadSummary(string explanationJson) =>
        JsonSerializer.Deserialize<Explanation>(explanationJson, LookupService.JsonOptions)?.Summary;
}
