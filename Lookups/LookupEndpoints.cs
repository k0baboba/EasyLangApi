using EasyLangApi.Vocabulary;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EasyLangApi.Lookups;

public static class LookupEndpoints
{
    public const int MaxPhraseLength = 100;

    public static IEndpointRouteBuilder MapLookupEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/lines/{id:int}/explain", ExplainAsync);
        return app;
    }

    private static async Task<Results<Ok<LookupResult>, NotFound, ProblemHttpResult>> ExplainAsync(
        int id, string? phrase, LookupService lookups, CancellationToken cancellationToken)
    {
        var trimmed = phrase?.Trim() ?? "";
        if (PhraseNormalizer.Normalize(trimmed).Length == 0)
            return TypedResults.Problem("Параметр 'phrase' должен содержать хотя бы одно слово.", statusCode: 400);
        if (trimmed.Length > MaxPhraseLength)
            return TypedResults.Problem($"Фраза длиннее {MaxPhraseLength} символов.", statusCode: 400);

        // ExplainerUnavailableException отсюда не ловим: её переводит в 503 KnownExceptionHandler.
        var result = await lookups.ExplainAsync(id, trimmed, cancellationToken);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
    }
}
