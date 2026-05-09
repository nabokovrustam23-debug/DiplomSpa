using System.Security.Cryptography;

namespace BarbershopCrm.Infrastructure.Auth;

public sealed class TokenGenerator : ITokenGenerator
{
    public string GenerateUrlSafeToken(int byteLength = 32)
    {
        if (byteLength < 16)
            throw new ArgumentOutOfRangeException(nameof(byteLength),
                "Token must be at least 16 bytes for security.");

        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return ToBase64Url(bytes);
    }

    private static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
