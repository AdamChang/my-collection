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

    /// <summary>要求「未設定」的 field key：該 key 不存在、為 null 或為空字串皆算符合。</summary>
    public IReadOnlyList<string>? MissingAttributes { get; init; }

    /// <summary>
    /// 候選品類的限縮，語意與 <see cref="CategoryId"/>（使用者選定的品類）不同：
    /// 這是由 schema 推導出的護欄，兩者同時存在時取交集。
    /// </summary>
    public IReadOnlyList<ObjectId>? CategoryIds { get; init; }
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

    /// <summary>自己在該品類下的品項數。刪除品類前的守門。</summary>
    Task<long> CountByCategoryAsync(ObjectId categoryId, CancellationToken ct);

    Task InsertAsync(Item item, CancellationToken ct);

    /// <summary>
    /// 更新品項本身的欄位，不碰 attributes 與 images。找不到（含不屬於自己）擲 NotFoundException。
    /// </summary>
    Task UpdateAsync(Item item, CancellationToken ct);

    /// <summary>
    /// 同上，並在同一次寫入中逐鍵套用 attributes 變更；未列在變更中的鍵（含未宣告屬性）原樣保留。
    /// </summary>
    Task UpdateAsync(Item item, AttributeChanges attributeChanges, CancellationToken ct);

    // 以下三個圖片寫入各自是單一原子操作（tech debt M1）：主圖與順序由資料庫依寫入當下的陣列決定，
    // 不採信呼叫端讀到的舊狀態。品項不存在擲 NotFoundException(Item)，圖片不存在擲 NotFoundException(ItemImage)。

    /// <summary>
    /// 附加一張圖。傳入的 IsPrimary／Order 會被忽略：陣列為空時成為主圖，order 為附加前的張數。
    /// </summary>
    /// <returns>寫入後的這張圖。</returns>
    Task<ItemImage> AddImageAsync(ObjectId itemId, ItemImage image, DateTime updatedAt, CancellationToken ct);

    /// <summary>移除一張圖；剩下的圖若沒有主圖，第一張晉升為主圖，order 依陣列位置重排。</summary>
    /// <returns>被移除的圖，供呼叫端刪檔。</returns>
    Task<ItemImage> RemoveImageAsync(ObjectId itemId, string imageId, DateTime updatedAt, CancellationToken ct);

    Task SetPrimaryImageAsync(ObjectId itemId, string imageId, DateTime updatedAt, CancellationToken ct);

    Task DeleteAsync(ObjectId id, CancellationToken ct);
}
