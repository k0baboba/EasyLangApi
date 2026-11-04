using EasyLangApi.Data;
using EasyLangApi.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyLangApi.Stats;

public static class StatsEndpoints
{
    public const int Days = 7;

    public sealed record DayStats(DateOnly Date, int Answers, int Correct);

    public sealed record StatsResult(int TotalItems, int KnownItems, IReadOnlyList<DayStats> Days);

    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/stats", GetAsync);
        return app;
    }

    /// <summary>Фильтр по пользователю добавляют глобальные фильтры AppDbContext.</summary>
    private static async Task<StatsResult> GetAsync(
        AppDbContext db, IOptions<ReviewOptions> options, TimeProvider time, CancellationToken cancellationToken)
    {
        var knownAfter = options.Value.KnownAfterDays;
        var total = await db.KnowledgeItems.CountAsync(cancellationToken);
        var known = await db.KnowledgeItems.CountAsync(k => k.IntervalDays >= knownAfter, cancellationToken);

        var today = time.GetUtcNow().UtcDateTime.Date;
        var from = today.AddDays(-(Days - 1));
        // Группировка по дню выполняется в SQL: date(AnsweredAt) ... GROUP BY.
        var perDay = await db.ReviewEvents
            .Where(e => e.AnsweredAt != null && e.AnsweredAt >= from)
            .GroupBy(e => e.AnsweredAt!.Value.Date)
            .Select(g => new { Day = g.Key, Answers = g.Count(), Correct = g.Count(e => e.WasCorrect == true) })
            .ToListAsync(cancellationToken);
        var byDay = perDay.ToDictionary(d => DateOnly.FromDateTime(d.Day), d => (d.Answers, d.Correct));

        var days = Enumerable.Range(0, Days)
            .Select(offset => DateOnly.FromDateTime(from.AddDays(offset)))
            .Select(day => byDay.TryGetValue(day, out var c) ? new DayStats(day, c.Answers, c.Correct) : new DayStats(day, 0, 0))
            .ToList();

        return new StatsResult(total, known, days);
    }
}
