namespace BarbershopCrm.Infrastructure.Security;

public interface IPasswordHasher
{
    PasswordHash Hash(string password);
    bool Verify(string password, string hashBase64, string saltBase64, int iterations);
}

public readonly record struct PasswordHash(string HashBase64, string SaltBase64, int Iterations);
