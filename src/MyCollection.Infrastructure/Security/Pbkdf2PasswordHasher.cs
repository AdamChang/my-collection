using System.Security.Cryptography;
using MyCollection.Application.Common;

namespace MyCollection.Infrastructure.Security;

/// <summary>
/// 格式：pbkdf2.{iterations}.{base64 salt}.{base64 key}
///
/// 迭代次數存在字串內，未來調高時舊雜湊仍可驗證，不必強制使用者改密碼。
/// 用 BCL 內建的 PBKDF2 而非 BCrypt/Argon2：各要多一個第三方相依，
/// 對個人站的威脅模型而言 PBKDF2-SHA256 210k 已足夠。
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 210_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);

        return $"pbkdf2.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string hash, string password)
    {
        var parts = hash.Split('.');
        if (parts.Length != 4 || parts[0] != "pbkdf2" || !int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);

        // 固定時間比較，避免時序側通道洩漏雜湊前綴
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
