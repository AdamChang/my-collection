using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Items;

/// <summary>Repository 層的查詢條件。ownerId 不在此，由 Repository 自 IUserContext 強制加上。</summary>
public sealed class ItemQuerySpec
{
    public string? Search { get; init; }
    public ObjectId? CategoryId { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public bool? IsShowcased { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 24;

    /// <summary>依 category schema 的 searchable 欄位篩選，key 為 field key、value 為精確比對值。</summary>
    public IReadOnlyDictionary<string, string>? Attributes { get; init; }
}

public interface IItemRepository
{
    Task<Item?> GetAsync(ObjectId id, CancellationToken ct);

    Task<PagedResult<Item>> SearchAsync(ItemQuerySpec spec, CancellationToken ct);

    Task<IReadOnlyList<string>> ListTagsAsync(CancellationToken ct);

    /// <summary>
    /// 相異的 platform 屬性值。categoryId 為 null 時不限品類，靠 attributes.platform 是否存在
    /// 自然限定範圍——只有宣告了 platform 欄位的品類，品項才可能有這個 key。
    /// </summary>
    Task<IReadOnlyList<string>> ListPlatformsAsync(ObjectId? categoryId, CancellationToken ct);

    /// <summary>
    /// 補完候選：有外部來源綁定（externalRef 非 null）、但 attributes 尚未帶 markerKey 的品項。
    /// 手動建檔且未綁定過的品項不在其中——補完不猜，那些應走搜尋建檔。
    /// </summary>
    Task<IReadOnlyList<Item>> ListEnrichmentCandidatesAsync(
        string markerKey, int limit, CancellationToken ct);

    /// <summary>依 id 批次載入自己的品項。不存在或不屬於自己的 id 直接不出現在結果中。</summary>
    Task<IReadOnlyList<Item>> ListByIdsAsync(IReadOnlyList<ObjectId> ids, CancellationToken ct);

    Task InsertAsync(Item item, CancellationToken ct);

    /// <summary>找不到（含不屬於自己）擲 NotFoundException。</summary>
    Task UpdateAsync(Item item, CancellationToken ct);

    Task DeleteAsync(ObjectId id, CancellationToken ct);
}
