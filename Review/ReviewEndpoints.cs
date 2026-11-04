using Microsoft.AspNetCore.Http.HttpResults;

namespace EasyLangApi.Review;

public static class ReviewEndpoints
{
    public sealed record AnswerRequest(bool? WasCorrect);

    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/review");
        group.MapGet("/next", NextAsync);
        group.MapPost("/{id:int}", AnswerAsync);
        return app;
    }

    private static async Task<Results<Ok<ReviewCard>, NoContent>> NextAsync(
        ReviewService reviews, CancellationToken cancellationToken)
    {
        var card = await reviews.NextAsync(cancellationToken);
        return card is null ? TypedResults.NoContent() : TypedResults.Ok(card);
    }

    /// <param name="id">ReviewId из ответа GET /review/next — конкретный показ.</param>
    private static async Task<Results<Ok<ReviewAnswer>, NotFound, ProblemHttpResult>> AnswerAsync(
        int id, AnswerRequest? request, ReviewService reviews, CancellationToken cancellationToken)
    {
        if (request?.WasCorrect is not { } wasCorrect)
            return TypedResults.Problem("Нужно тело {\"wasCorrect\": true|false}.", statusCode: 400);

        var outcome = await reviews.AnswerAsync(id, wasCorrect, cancellationToken);
        return outcome.Status switch
        {
            AnswerStatus.Accepted => TypedResults.Ok(outcome.Answer!),
            AnswerStatus.NotFound => TypedResults.NotFound(),
            AnswerStatus.AlreadyAnswered => TypedResults.Problem(
                "На этот показ уже ответили. Запросите GET /review/next.", statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException($"Неизвестный статус {outcome.Status}"),
        };
    }
}
