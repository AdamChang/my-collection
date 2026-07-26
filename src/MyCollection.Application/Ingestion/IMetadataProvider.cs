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

    /// <summary>可依關鍵字搜尋（第一版無實作，供未來 Discogs / IGDB 使用）。</summary>
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

public interface IMetadataProvider
{
    /// <summary>"steam" | "opengraph"。全小寫。</summary>
    string Key { get; }

    ProviderCapability Capabilities { get; }

    /// <summary>失敗時擲 <see cref="Domain.Exceptions.ProviderException"/>。</summary>
    Task<IReadOnlyList<ExternalItem>> SyncAsync(ExternalAccount account, CancellationToken ct);

    /// <summary>抓不到可用中繼資料時回傳 null。</summary>
    Task<ExternalItem?> FetchByUrlAsync(Uri url, CancellationToken ct);
}
