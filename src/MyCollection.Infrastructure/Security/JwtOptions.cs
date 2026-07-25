namespace MyCollection.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "mycollection";
    public string Audience { get; init; } = "mycollection-web";

    /// <summary>HMAC-SHA256 簽章金鑰，至少 32 bytes。正式環境以環境變數提供。</summary>
    public string Key { get; init; } = string.Empty;

    public int AccessTokenMinutes { get; init; } = 30;
    public int RefreshTokenDays { get; init; } = 14;
}
