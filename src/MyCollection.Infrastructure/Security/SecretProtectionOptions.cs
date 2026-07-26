namespace MyCollection.Infrastructure.Security;

public sealed class SecretProtectionOptions
{
    public const string SectionName = "SecretProtection";

    /// <summary>Base64 編碼的 32-byte 金鑰。正式環境以環境變數 SecretProtection__Key 提供。</summary>
    public string Key { get; init; } = string.Empty;
}
