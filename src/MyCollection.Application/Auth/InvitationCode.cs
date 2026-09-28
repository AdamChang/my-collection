using System.Security.Cryptography;
using System.Text;

namespace MyCollection.Application.Auth;

/// <summary>
/// 邀請碼：Crockford base32、20 字元（100 bit）。
/// I1（認證端點沒有速率限制）解決之前，碼的強度是防暴力猜測的唯一防線，不可縮短（ADR-0013 §三）。
/// </summary>
public static class InvitationCode
{
    public const int Length = 20;
    private const int GroupSize = 5;

    // Crockford 字母表刻意排除 I、L、O、U，避免手動輸入時混淆
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);

    /// <summary>
    /// 依 Crockford 解碼規則正規化：不分大小寫、忽略連字號與空白，I／L 讀作 1、O 讀作 0。
    /// 產生的碼不含這些字元，所以這個對應只會救回打錯的輸入，不會造成碰撞。
    /// </summary>
    public static string Normalise(string input)
    {
        var builder = new StringBuilder(input.Length);

        foreach (var c in input.ToUpperInvariant())
        {
            if (c == '-' || char.IsWhiteSpace(c))
            {
                continue;
            }

            builder.Append(c switch
            {
                'I' or 'L' => '1',
                'O' => '0',
                _ => c
            });
        }

        return builder.ToString();
    }

    public static string Format(string normalised) =>
        string.Join('-', normalised.Chunk(GroupSize).Select(chunk => new string(chunk)));
}
