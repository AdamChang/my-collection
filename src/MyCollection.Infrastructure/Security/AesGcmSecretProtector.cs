using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MyCollection.Application.Common;

namespace MyCollection.Infrastructure.Security;

/// <summary>密文格式：base64(nonce[12] || tag[16] || ciphertext)。</summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public AesGcmSecretProtector(IOptions<SecretProtectionOptions> options)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(options.Value.Key);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("SecretProtection:Key must be Base64-encoded.", ex);
        }

        if (key.Length != 32)
        {
            throw new InvalidOperationException("SecretProtection:Key must decode to exactly 32 bytes.");
        }

        _key = key;
    }

    public string Protect(string plaintext)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipherBytes = new byte[plainBytes.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var payload = new byte[NonceSize + TagSize + cipherBytes.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, NonceSize);
        cipherBytes.CopyTo(payload, NonceSize + TagSize);

        return Convert.ToBase64String(payload);
    }

    public string Unprotect(string ciphertext)
    {
        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(ciphertext);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Ciphertext is not valid Base64.", ex);
        }

        if (payload.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Ciphertext is too short.");
        }

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipherBytes = payload.AsSpan(NonceSize + TagSize);
        var plainBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
