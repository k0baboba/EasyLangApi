using Microsoft.IdentityModel.JsonWebTokens;

namespace EasyLangApi.Auth;

/// <summary>Кто делает текущий запрос. null — анонимный запрос или работа вне HTTP (миграции).</summary>
public interface ICurrentUser
{
    int? UserId { get; }
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public int? UserId =>
        int.TryParse(accessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;
}
