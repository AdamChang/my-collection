namespace MyCollection.Infrastructure.Security;

public sealed class SecretProtectionOptions
{
    public const string SectionName = "SecretProtection";

    /// <summary>Base64 編碼的 32-byte 金鑰。正式環境由 Secret Manager api-config 的 JSON 提供（ADR-0013）。</summary>
    public string Key { get; init; } = string.Empty;
}
