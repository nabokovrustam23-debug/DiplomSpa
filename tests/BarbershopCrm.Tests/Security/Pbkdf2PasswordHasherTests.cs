using BarbershopCrm.Infrastructure.Security;
using FluentAssertions;
using Xunit;

namespace BarbershopCrm.Tests.Security;

public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new(iterations: 10_000);

    [Fact]
    public void Hash_ProducesNonEmpty_HashSaltAndIterations()
    {
        var result = _hasher.Hash("Test12345!");

        result.HashBase64.Should().NotBeNullOrWhiteSpace();
        result.SaltBase64.Should().NotBeNullOrWhiteSpace();
        result.Iterations.Should().Be(10_000);

        Convert.FromBase64String(result.HashBase64).Length.Should().Be(Pbkdf2PasswordHasher.HashSizeBytes);
        Convert.FromBase64String(result.SaltBase64).Length.Should().Be(Pbkdf2PasswordHasher.SaltSizeBytes);
    }

    [Fact]
    public void Verify_ReturnsTrue_ForOriginalPassword()
    {
        var hashed = _hasher.Hash("Test12345!");

        _hasher.Verify("Test12345!", hashed.HashBase64, hashed.SaltBase64, hashed.Iterations)
            .Should().BeTrue();
    }

    [Fact]
    public void Verify_ReturnsFalse_ForWrongPassword()
    {
        var hashed = _hasher.Hash("Test12345!");

        _hasher.Verify("wrong-password", hashed.HashBase64, hashed.SaltBase64, hashed.Iterations)
            .Should().BeFalse();
    }

    [Fact]
    public void Hash_IsRandomized_ProducesDifferentSalt()
    {
        var a = _hasher.Hash("same-password");
        var b = _hasher.Hash("same-password");

        a.SaltBase64.Should().NotBe(b.SaltBase64);
        a.HashBase64.Should().NotBe(b.HashBase64);
    }

    [Fact]
    public void Verify_ReturnsFalse_OnMalformedBase64()
    {
        _hasher.Verify("any", "not-base64-!!!", "also-bad-!!!", 10_000)
            .Should().BeFalse();
    }
}
