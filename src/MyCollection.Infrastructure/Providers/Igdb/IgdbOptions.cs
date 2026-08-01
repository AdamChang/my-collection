using MyCollection.Application.Ingestion;

namespace MyCollection.Infrastructure.Providers.Igdb;

public sealed class IgdbOptions
{
    public const string SectionName = "Igdb";

    /// <summary>與 <see cref="ProviderKeys.Igdb"/> 相同；放在這裡讓 Infrastructure 內部不必互相引用類別。</summary>
    public const string ProviderKey = ProviderKeys.Igdb;

    /// <summary>Twitch 應用程式的 Client ID。空值代表整個 IGDB 功能停用。</summary>
    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    public string TokenBaseAddress { get; init; } = "https://id.twitch.tv/";
    public string BaseAddress { get; init; } = "https://api.igdb.com/v4/";

    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>單次搜尋回傳上限。</summary>
    public int SearchLimit { get; init; } = 20;

    /// <summary>批次反查時一次帶幾個外部 id。</summary>
    public int LookupBatchSize { get; init; } = 10;

    /// <summary>
    /// 兩次 IGDB 請求之間的最小間隔。IGDB 限制 4 req/sec，
    /// 超標的懲罰是整段時間被擋，代價不對稱，所以自我節流而非撞到 429 才退避。
    /// </summary>
    public int MinRequestIntervalMs { get; init; } = 250;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
