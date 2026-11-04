using EasyLangApi.Explaining;
using Microsoft.AspNetCore.Diagnostics;

namespace EasyLangApi.Infrastructure;

/// <summary>
/// Переводит ожидаемые исключения (внешний сервис недоступен) в ответ ProblemDetails
/// с осмысленным кодом. Остальные исключения не трогает — они станут 500.
/// </summary>
public sealed class KnownExceptionHandler(
    IProblemDetailsService problemDetails, ILogger<KnownExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        (int Status, string Title)? known = exception switch
        {
            ExplainerUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Сервис разбора фраз недоступен"),
            _ => null,
        };
        if (known is null)
            return false;

        logger.LogWarning(exception, "{Title}", known.Value.Title);
        httpContext.Response.StatusCode = known.Value.Status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = known.Value.Status,
                Title = known.Value.Title,
                Detail = exception.Message,
            },
        });
    }
}
