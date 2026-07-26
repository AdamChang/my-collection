using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MyCollection.Infrastructure.Security;

namespace MyCollection.Tests.Unit;

public class AesGcmSecretProtectorTests
{
    private static AesGcmSecretProtector CreateSut(string? key = null) =>
        new(Options.Create(new SecretProtectionOptions
        {
            Key = key ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }));

    [Fact]
    public void Protect_then_Unprotect_roundtrips()
    {
        var sut = CreateSut();

        var cipher = sut.Protect("steam-api-key-1234567890");

        cipher.Should().NotContain("steam-api-key");
        sut.Unprotect(cipher).Should().Be("steam-api-key-1234567890");
    }

    [Fact]
    public void Protect_uses_a_fresh_nonce_each_time()
    {
        var sut = CreateSut();

        sut.Protect("same").Should().NotBe(sut.Protect("same"));
    }

    [Fact]
    public void Unprotect_with_a_different_key_throws()
    {
        var cipher = CreateSut().Protect("secret");

        var act = () => CreateSut().Unprotect(cipher);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Unprotect_rejects_tampered_payload()
    {
        var sut = CreateSut();
        var cipher = sut.Protect("secret");
        var bytes = Convert.FromBase64String(cipher);
        bytes[^1] ^= 0xFF;

        var act = () => sut.Unprotect(Convert.ToBase64String(bytes));

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Constructor_rejects_a_key_that_is_not_32_bytes()
    {
        var act = () => CreateSut(Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)));

        act.Should().Throw<InvalidOperationException>();
    }
}
