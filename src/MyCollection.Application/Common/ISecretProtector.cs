namespace MyCollection.Application.Common;

/// <summary>外部服務憑證的對稱加密。密文以 Base64 存進文件。</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    /// <summary>金鑰不符或密文被竄改時擲 <see cref="System.Security.Cryptography.CryptographicException"/>。</summary>
    string Unprotect(string ciphertext);
}
