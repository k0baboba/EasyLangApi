using Microsoft.IdentityModel.JsonWebTokens;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace EasyLangApi.Quotas;

public static class RateLimitingExtensions
{
    /// <summary>Имя политики для эндпоинтов, которые ходят во внешние сервисы или запускают Tesseract.</summary>
    public const string ExpensivePolicy = "expensive";

    public static IServiceCollection AddQuotas(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<QuotaOptions>(configuration.GetSection(QuotaOptions.SectionName));

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Окно на каждого пользователя своё: ключ раздела — id из токена (для анонимных — IP).
            limiter.AddPolicy(ExpensivePolicy, httpContext =>
            {
                var userId = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                var key = userId is not null ? $"user:{userId}" : $"ip:{httpContext.Connection.RemoteIpAddress}";
                var settings = httpContext.RequestServices.GetRequiredService<IOptions<QuotaOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.ExpensiveRequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
            limiter.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

                var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problems.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Слишком много запросов",
                        Detail = "Лимит запросов к разбору и распознаванию исчерпан, повторите через минуту.",
                    },
                });
            };
        });

        return services;
    }
}
