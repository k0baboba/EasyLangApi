namespace EasyLangApi.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "EasyLangApi";
    public string Audience { get; set; } = "EasyLangApi";

    /// <summary>
    /// Секрет для подписи HMAC-SHA256, не короче 32 байт. Если пусто — при запуске
    /// генерируется случайный: access-токены живут до перезапуска, refresh-токены (в базе) переживают его.
    /// </summary>
    public string? SigningKey { get; set; }

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}
