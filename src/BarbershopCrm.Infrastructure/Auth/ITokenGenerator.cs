namespace BarbershopCrm.Infrastructure.Auth;

public interface ITokenGenerator
{
    /// <summary>
    /// Generates a 32-byte cryptographically random token, encoded base64url (no padding).
    /// Suitable for session tokens and one-time URL tokens.
    /// </summary>
    string GenerateUrlSafeToken(int byteLength = 32);
}
