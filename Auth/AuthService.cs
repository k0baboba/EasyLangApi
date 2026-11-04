using EasyLangApi.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyLangApi.Auth;

public enum AuthStatus
{
    Ok,
    InvalidInput,
    EmailTaken,
    InvalidCredentials,
}

public sealed record AuthOutcome(AuthStatus Status, TokenPair? Tokens = null, string? Error = null);

/// <summary>Регистрация, вход, обновление и отзыв токенов.</summary>
public sealed class AuthService(
    AppDbContext db, TokenService tokens, IPasswordHasher<User> hasher, TimeProvider time, ILogger<AuthService> logger)
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;

    public async Task<AuthOutcome> RegisterAsync(string? email, string? password, CancellationToken cancellationToken)
    {
        var normalized = NormalizeEmail(email);
        if (normalized is null)
            return new AuthOutcome(AuthStatus.InvalidInput, Error: "Нужен корректный email.");
        if (password is null || password.Length < MinPasswordLength || password.Length > MaxPasswordLength)
            return new AuthOutcome(AuthStatus.InvalidInput,
                Error: $"Пароль — от {MinPasswordLength} до {MaxPasswordLength} символов.");
        if (await db.Users.AnyAsync(u => u.Email == normalized, cancellationToken))
            return new AuthOutcome(AuthStatus.EmailTaken, Error: "Пользователь с таким email уже есть.");

        var user = new User { Email = normalized, CreatedAt = time.GetUtcNow().UtcDateTime };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return new AuthOutcome(AuthStatus.Ok, await IssueAsync(user, cancellationToken));
    }

    public async Task<AuthOutcome> LoginAsync(string? email, string? password, CancellationToken cancellationToken)
    {
        var normalized = NormalizeEmail(email);
        var user = normalized is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken);
        // Одинаковый ответ на "нет пользователя" и "неверный пароль" — чтобы нельзя было перебирать email.
        if (user is null || string.IsNullOrEmpty(password) ||
            hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
            return new AuthOutcome(AuthStatus.InvalidCredentials);

        return new AuthOutcome(AuthStatus.Ok, await IssueAsync(user, cancellationToken));
    }

    /// <summary>
    /// Обменивает refresh-токен на новую пару. Старый отзывается. Если пришёл уже отозванный
    /// токен — его, скорее всего, украли: отзываем все активные токены пользователя.
    /// </summary>
    public async Task<AuthOutcome> RefreshAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return new AuthOutcome(AuthStatus.InvalidCredentials);

        var now = time.GetUtcNow().UtcDateTime;
        var hash = TokenService.Hash(refreshToken);
        var stored = await db.RefreshTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (stored is null || stored.ExpiresAt <= now)
            return new AuthOutcome(AuthStatus.InvalidCredentials);

        if (stored.RevokedAt is not null)
        {
            logger.LogWarning("Повторное использование отозванного refresh-токена пользователя {UserId}", stored.UserId);
            await db.RefreshTokens
                .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, now), cancellationToken);
            return new AuthOutcome(AuthStatus.InvalidCredentials);
        }

        stored.RevokedAt = now;
        return new AuthOutcome(AuthStatus.Ok, await IssueAsync(stored.User, cancellationToken));
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return;

        var hash = TokenService.Hash(refreshToken);
        var now = time.GetUtcNow().UtcDateTime;
        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, now), cancellationToken);
    }

    private async Task<TokenPair> IssueAsync(User user, CancellationToken cancellationToken)
    {
        var (access, accessExpires) = tokens.CreateAccessToken(user);
        var (refresh, refreshHash, refreshExpires) = tokens.CreateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            CreatedAt = time.GetUtcNow().UtcDateTime,
            ExpiresAt = refreshExpires,
        });
        await db.SaveChangesAsync(cancellationToken);
        return new TokenPair(access, accessExpires, refresh, refreshExpires);
    }

    private static string? NormalizeEmail(string? email)
    {
        var trimmed = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 254)
            return null;
        var at = trimmed.IndexOf('@');
        return at > 0 && at == trimmed.LastIndexOf('@') && at < trimmed.Length - 1 ? trimmed : null;
    }
}
