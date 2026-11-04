using System.Text;
using EasyLangApi.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EasyLangApi.Auth;

public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddJwtAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => string.IsNullOrEmpty(o.SigningKey) || Encoding.UTF8.GetByteCount(o.SigningKey) >= SigningKeyProvider.MinKeyBytes,
                $"Jwt:SigningKey должен быть не короче {SigningKeyProvider.MinKeyBytes} байт.")
            .Validate(o => o.AccessTokenMinutes > 0 && o.RefreshTokenDays > 0, "Сроки жизни токенов должны быть положительными.")
            .ValidateOnStart();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddSingleton<SigningKeyProvider>();
        services.AddSingleton<TokenService>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<AuthService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Параметры проверки настраиваются при первом запросе, а не при старте:
        // так они видят ключ из SigningKeyProvider и подменённые в тестах TimeProvider и конфигурацию.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, SigningKeyProvider, TimeProvider>((bearer, jwt, keys, time) =>
            {
                bearer.MapInboundClaims = false; // оставить "sub" как есть, без переименования в длинный URI
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = keys.Key,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    // Срок жизни сверяем с тем же TimeProvider, которым токен выпущен.
                    LifetimeValidator = (notBefore, expires, _, _) =>
                    {
                        var now = time.GetUtcNow().UtcDateTime;
                        var skew = TimeSpan.FromSeconds(30);
                        return (notBefore is null || notBefore.Value <= now + skew)
                               && expires is not null && expires.Value > now - skew;
                    },
                };
            });

        // Всё, что явно не помечено AllowAnonymous, требует входа.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }
}
