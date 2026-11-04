using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EasyLangApi.Auth;

/// <summary>Один ключ подписи на всё приложение: им подписываем при выдаче и проверяем при входе.</summary>
public sealed class SigningKeyProvider
{
    public const int MinKeyBytes = 32;

    public SigningKeyProvider(IOptions<JwtOptions> options, ILogger<SigningKeyProvider> logger)
    {
        var configured = options.Value.SigningKey;
        if (string.IsNullOrWhiteSpace(configured))
        {
            logger.LogWarning("Jwt:SigningKey не задан — сгенерирован временный ключ. " +
                              "Access-токены перестанут действовать после перезапуска.");
            Key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(MinKeyBytes));
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(configured);
        if (bytes.Length < MinKeyBytes)
            throw new InvalidOperationException($"Jwt:SigningKey должен быть не короче {MinKeyBytes} байт.");
        Key = new SymmetricSecurityKey(bytes);
    }

    public SymmetricSecurityKey Key { get; }
}
