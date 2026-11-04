using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EasyLangApi.Auth;

public static class AuthEndpoints
{
    public sealed record Credentials(string? Email, string? Password);

    public sealed record RefreshRequest(string? RefreshToken);

    public sealed record Me(int Id, string? Email);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");
        group.MapPost("/register", RegisterAsync).AllowAnonymous();
        group.MapPost("/login", LoginAsync).AllowAnonymous();
        group.MapPost("/refresh", RefreshAsync).AllowAnonymous();
        group.MapPost("/logout", LogoutAsync).AllowAnonymous();
        group.MapGet("/me", (ClaimsPrincipal user) => new Me(
            int.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value),
            user.FindFirst(JwtRegisteredClaimNames.Email)?.Value));
        return app;
    }

    private static async Task<Results<Created<TokenPair>, ProblemHttpResult>> RegisterAsync(
        Credentials request, AuthService auth, CancellationToken cancellationToken)
    {
        var outcome = await auth.RegisterAsync(request.Email, request.Password, cancellationToken);
        return outcome.Status switch
        {
            AuthStatus.Ok => TypedResults.Created("/auth/me", outcome.Tokens!),
            AuthStatus.EmailTaken => TypedResults.Problem(outcome.Error, statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Problem(outcome.Error, statusCode: StatusCodes.Status400BadRequest),
        };
    }

    private static async Task<Results<Ok<TokenPair>, ProblemHttpResult>> LoginAsync(
        Credentials request, AuthService auth, CancellationToken cancellationToken)
    {
        var outcome = await auth.LoginAsync(request.Email, request.Password, cancellationToken);
        return outcome.Status == AuthStatus.Ok
            ? TypedResults.Ok(outcome.Tokens!)
            : TypedResults.Problem("Неверный email или пароль.", statusCode: StatusCodes.Status401Unauthorized);
    }

    private static async Task<Results<Ok<TokenPair>, ProblemHttpResult>> RefreshAsync(
        RefreshRequest request, AuthService auth, CancellationToken cancellationToken)
    {
        var outcome = await auth.RefreshAsync(request.RefreshToken, cancellationToken);
        return outcome.Status == AuthStatus.Ok
            ? TypedResults.Ok(outcome.Tokens!)
            : TypedResults.Problem("Refresh-токен недействителен, войдите заново.", statusCode: StatusCodes.Status401Unauthorized);
    }

    private static async Task<NoContent> LogoutAsync(
        RefreshRequest request, AuthService auth, CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(request.RefreshToken, cancellationToken);
        return TypedResults.NoContent();
    }
}
