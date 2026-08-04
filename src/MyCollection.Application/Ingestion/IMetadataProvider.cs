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

    /// <summary>可依關鍵字搜尋。</summary>
    Search = 4,

    /// <summary>可依外部識別碼反查，用於補完既有品項。</summary>
    Enrich = 8
}

/// <summary>
/// 補完時會寫到的非 attribute 欄位。與 attribute key 共用同一個軟寫入集合，
/// 前綴 '#' 是為了不可能與任何 attribute key 相撞。
/// </summary>
public static class ItemFieldKeys
{
    public const string Name = "#name";
    public const string Description = "#description";
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

    /// <summary>
    /// 欄位擁有權：列在這裡的欄位只在品項尚無值時寫入，其餘一律覆蓋。
    /// key 是 attribute key，或 <see cref="ItemFieldKeys"/> 的兩個常數。
    ///
    /// 存在的理由是同一個 attribute 有多個 provider 想寫（例如 genres：
    /// Steam 商店寫繁體中文、IGDB 寫英文）。讓其中一方讓位，
    /// 結果就與執行順序無關；兩邊都無條件寫則是漂移的來源。
    /// </summary>
    public IReadOnlySet<string> FillOnlyIfAbsent { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);
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

/// <summary>
/// 可依外部識別碼反查，供 EnrichCommand 補完既有品項。
///
/// 兩個 key 刻意分開：Steam 商店補完的識別碼來自 steamAppId（IGDB 反查時也會寫），
/// 但「這筆已補完過」得靠另一個只有它自己會寫的欄位判斷。
/// 合成一個 key 的話，IGDB 一寫 steamAppId 就等於宣告 Steam 補完做完了，
/// 該品項會被批次永久跳過。
/// </summary>
public interface IExternalIdLookupProvider : IMetadataProvider
{
    /// <summary>組外部識別碼（"steam:292030"）的來源 attribute key。</summary>
    string ExternalIdAttributeKey { get; }

    /// <summary>批次補完排除已處理品項的依據；只有本 provider 會寫入。</summary>
    string CompletionMarkerKey { get; }

    /// <summary>寫入 attributes 時，目標品類必須宣告的欄位。</summary>
    IReadOnlyList<CategoryField> RequiredFields { get; }

    /// <summary>
    /// 反查成本高到不該綁在 HTTP 請求生命週期內時為 true，補完會改丟背景佇列。
    /// 這是 provider 的性質（自身的速率限制與是否支援批次），不是呼叫端的選擇，
    /// 所以宣告在這裡而不是由指令參數決定。
    /// </summary>
    bool PrefersBackgroundExecution { get; }

    /// <summary>
    /// 以 "steam:440" / "igdb:1942" 形式的外部識別碼批次反查，內部自行分塊與節流。
    /// 查無對應者不出現在 Found；請求層級失敗者列入 FailedIds。
    /// </summary>
    Task<ExternalLookupResult> FetchByExternalIdsAsync(
        IReadOnlyList<string> externalIds, CancellationToken ct);
}

public interface ISearchProvider : IExternalIdLookupProvider
{
    /// <summary>失敗時擲 <see cref="Domain.Exceptions.ProviderException"/>。</summary>
    Task<IReadOnlyList<ExternalItem>> SearchAsync(string query, int limit, CancellationToken ct);
}
