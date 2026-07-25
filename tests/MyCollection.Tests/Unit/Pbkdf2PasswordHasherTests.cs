using FluentAssertions;
using MyCollection.Infrastructure.Security;

namespace MyCollection.Tests.Unit;

public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _sut = new();

    [Fact]
    public void Hash_produces_different_output_for_same_password()
    {
        var a = _sut.Hash("P@ssw0rd!");
        var b = _sut.Hash("P@ssw0rd!");

        a.Should().NotBe(b, "每次雜湊都應使用新的 salt");
    }

    [Fact]
    public void Hash_never_contains_the_plaintext()
    {
        _sut.Hash("P@ssw0rd!").Should().NotContain("P@ssw0rd!");
    }

    [Fact]
    public void Hash_records_the_iteration_count_so_the_cost_can_be_raised_later()
    {
        var parts = _sut.Hash("P@ssw0rd!").Split('.');

        parts.Should().HaveCount(4);
        parts[0].Should().Be("pbkdf2");
        int.Parse(parts[1]).Should().BeGreaterThanOrEqualTo(210_000);
    }

    [Fact]
    public void Verify_returns_true_for_correct_password()
    {
        var hash = _sut.Hash("P@ssw0rd!");

        _sut.Verify(hash, "P@ssw0rd!").Should().BeTrue();
    }

    [Fact]
    public void Verify_returns_false_for_wrong_password()
    {
        var hash = _sut.Hash("P@ssw0rd!");

        _sut.Verify(hash, "wrong").Should().BeFalse();
    }

    [Fact]
    public void Verify_is_case_sensitive()
    {
        var hash = _sut.Hash("P@ssw0rd!");

        _sut.Verify(hash, "p@ssw0rd!").Should().BeFalse();
    }

    [Fact]
    public void Verify_still_accepts_hashes_written_with_a_lower_iteration_count()
    {
        // 模擬提高迭代次數之前寫入的舊雜湊
        var legacy = BuildLegacyHash("P@ssw0rd!", iterations: 1000);

        _sut.Verify(legacy, "P@ssw0rd!").Should().BeTrue("迭代次數存在字串裡就是為了讓舊雜湊仍可驗證");
        _sut.Verify(legacy, "wrong").Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("bcrypt.1.2.3")]
    [InlineData("pbkdf2.notanumber.c2FsdA==.a2V5")]
    [InlineData("pbkdf2.1000.!!!notbase64!!!.a2V5")]
    [InlineData("pbkdf2.1000.c2FsdA==")]
    public void Verify_returns_false_for_malformed_hash(string hash)
    {
        // 資料庫欄位被手動改壞不該讓登入端點擲例外
        _sut.Verify(hash, "P@ssw0rd!").Should().BeFalse();
    }

    private static string BuildLegacyHash(string password, int iterations)
    {
        var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        var key = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);

        return $"pbkdf2.{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }
}
