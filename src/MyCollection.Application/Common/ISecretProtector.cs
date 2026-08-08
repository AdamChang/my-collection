namespace MyCollection.Application.Common;

/// <summary>外部服務憑證的對稱加密。密文以 Base64 存進文件。</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    /// <summary>
    /// 金鑰不符、密文被竄改或格式壞掉時擲
    /// <see cref="MyCollection.Domain.Exceptions.UnreadableCredentialException"/>——
    /// 三種情形使用者能做的都是重新綁定，呼叫端不必分辨。
    /// </summary>
    string Unprotect(string ciphertext);
}
