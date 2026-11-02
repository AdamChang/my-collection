namespace MyCollection.Api;

/// <summary>
/// 正式環境把 Jwt、SecretProtection、Igdb 的機密合併在單一 JSON（Secret Manager `api-config`），
/// 由 Cloud Run secret volume 掛載成檔案（ADR-0013）。
/// </summary>
public static class SecretsFileConfiguration
{
    public const string PathKey = "SECRETS_CONFIG_FILE";

    public static IConfigurationBuilder AddSecretsFile(this IConfigurationBuilder builder, string? path)
    {
        // 本機與 docker compose 不設定路徑，照舊使用 user secrets 或環境變數。
        if (string.IsNullOrWhiteSpace(path))
        {
            return builder;
        }

        var fullPath = Path.GetFullPath(path);

        // 有指定路徑卻找不到檔案，代表掛載失敗；讓啟動直接失敗，canary 才擋得下來，
        // 而不是帶著空的金鑰繼續執行。
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Secrets config file not found: {fullPath}", fullPath);
        }

        return builder.AddJsonFile(fullPath, optional: false, reloadOnChange: false);
    }
}
