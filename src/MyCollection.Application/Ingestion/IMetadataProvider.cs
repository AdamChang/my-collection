using MyCollection.Domain.Entities;

namespace MyCollection.Application.Ingestion;

[Flags]
public enum ProviderCapability
{
    None = 0,

    /// <summary>可用已綁定帳號一次拉回全部品項。</summary>
    BulkSync = 1,

    /// <summary>可從單一 URL 擷取品項資料。</summary>
    UrlLookup = 2,

    /// <summary>可依關鍵字搜尋，並以外部識別碼反查。</summary>
    Search = 4
}

/// <summary>Provider 回傳的中性結構，尚未綁定任何品類 schema。</summary>
public record ExternalItem(
    string ExternalId,
    string Name,
    string? Description,
    Uri? ImageUrl,
    IReadOnlyDictionary<string, object?> Attributes)
{
    public Uri? SourceUrl { get; init; }
}

/// <summary>Found 的 key 是傳入的 externalId。三種結果互斥：命中、查無、請求失敗。</summary>
public record ExternalLookupResult(
    IReadOnlyDictionary<string, ExternalItem> Found,
    IReadOnlyList<string> FailedIds);

/// <summary>
/// 所有 provider 的共同基底，只帶識別。能力由下方三個介面表達。
/// 舊版把三種能力塞在同一個介面，逼每個 provider 實作用不到的樁，
/// 且 Capabilities 旗標與實際實作是兩處來源，會漂移。
/// </summary>
public interface IMetadataProvider
{
    /// <summary>見 <see cref="ProviderKeys"/>。全小寫。</summary>
    string Key { get; }
}

public interface IBulkSyncProvider : IMetadataProvider
{
    /// <summary>失敗時擲 <see cref="Domain.Exceptions.ProviderException"/>。</summary>
    Task<IReadOnlyList<ExternalItem>> SyncAsync(ExternalAccount account, CancellationToken ct);
}

public interface IUrlLookupProvider : IMetadataProvider
{
    /// <summary>抓不到可用中繼資料時回傳 null。</summary>
    Task<ExternalItem?> FetchByUrlAsync(Uri url, CancellationToken ct);
}

public interface ISearchProvider : IMetadataProvider
{
    /// <summary>標記「此品項已綁定本 provider」的 attribute key，也是批次補完的篩選依據。</summary>
    string MarkerAttributeKey { get; }

    /// <summary>寫入 attributes 時，目標品類必須宣告的欄位。</summary>
    IReadOnlyList<CategoryField> RequiredFields { get; }

    /// <summary>失敗時擲 <see cref="Domain.Exceptions.ProviderException"/>。</summary>
    Task<IReadOnlyList<ExternalItem>> SearchAsync(string query, int limit, CancellationToken ct);

    /// <summary>
    /// 以 "steam:440" / "igdb:1942" 形式的外部識別碼批次反查，內部自行分塊與節流。
    /// 查無對應者不出現在 Found；請求層級失敗者列入 FailedIds。
    /// </summary>
    Task<ExternalLookupResult> FetchByExternalIdsAsync(
        IReadOnlyList<string> externalIds, CancellationToken ct);
}
