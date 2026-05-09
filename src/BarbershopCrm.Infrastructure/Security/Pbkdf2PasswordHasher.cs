using System.Security.Cryptography;

namespace BarbershopCrm.Infrastructure.Security;

/// <summary>
/// Собственная реализация хеширования паролей на PBKDF2-HMAC-SHA256.
/// Не использует ASP.NET Core Identity.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public const int DefaultIterations = 100_000;
    public const int SaltSizeBytes = 16;
    public const int HashSizeBytes = 32;

    private readonly int _iterations;

    public Pbkdf2PasswordHasher(int iterations = DefaultIterations)
    {
        if (iterations < 10_000)
            throw new ArgumentOutOfRangeException(nameof(iterations),
                "PBKDF2 iterations must be >= 10 000.");
        _iterations = iterations;
    }

    public PasswordHash Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            _iterations,
            HashAlgorithmName.SHA256,
            HashSizeBytes);

        return new PasswordHash(
            Convert.ToBase64String(hash),
            Convert.ToBase64String(salt),
            _iterations);
    }

    public bool Verify(string password, string hashBase64, string saltBase64, int iterations)
    {
        if (string.IsNullOrEmpty(password) ||
            string.IsNullOrEmpty(hashBase64) ||
            string.IsNullOrEmpty(saltBase64))
        {
            return false;
        }

        byte[] expected;
        byte[] salt;
        try
        {
            expected = Convert.FromBase64String(hashBase64);
            salt = Convert.FromBase64String(saltBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        if (expected.Length != HashSizeBytes || salt.Length != SaltSizeBytes)
            return false;

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashSizeBytes);

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
