using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Transfer;

/// <summary>
/// 匯出／匯入專用的跨 collection 存取。與一般 Repository 分開，
/// 因為它需要「只取自訂品類」「排除 Steam 來源」這類匯出獨有的條件，
/// 混進 ICategoryRepository/IItemRepository 會汙染日常查詢的語意。
///
/// 所有方法的 filter 一律以 IUserContext.UserId 起頭。
/// </summary>
public interface ITransferRepository
{
    // ---- 匯出 ----

    /// <summary>只取自訂品類（OwnerId == me）。系統品類 OwnerId 為 null，自動排除。</summary>
    Task<IReadOnlyList<Category>> ListOwnCategoriesAsync(CancellationToken ct);

    /// <summary>Source != Steam。OpenGraph 來源視為手建，要匯出。</summary>
    Task<IReadOnlyList<Item>> ListExportableItemsAsync(CancellationToken ct);

    Task<IReadOnlyList<ShareLink>> ListOwnShareLinksAsync(CancellationToken ct);

    // ---- 匯入 ----

    /// <summary>Source == Steam。匯入時保留，用於判定孤兒品類。</summary>
    Task<IReadOnlyList<Item>> ListSteamItemsAsync(CancellationToken ct);

    Task DeleteNonSteamItemsAsync(CancellationToken ct);

    Task DeleteOwnShareLinksAsync(CancellationToken ct);

    Task DeleteCategoriesAsync(IReadOnlyList<ObjectId> ids, CancellationToken ct);

    /// <summary>把指定 item 的 CategoryId 改指到 targetCategoryId。</summary>
    Task RepointItemsAsync(IReadOnlyList<ObjectId> itemIds, ObjectId targetCategoryId, CancellationToken ct);

    Task InsertCategoriesAsync(IReadOnlyList<Category> categories, CancellationToken ct);

    Task InsertItemsAsync(IReadOnlyList<Item> items, CancellationToken ct);

    Task InsertShareLinksAsync(IReadOnlyList<ShareLink> links, CancellationToken ct);

    /// <summary>slug 全域唯一，此查詢刻意不套 ownerId 過濾。</summary>
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct);
}
