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

    /// <summary>
    /// 傳入的 id 當中，已被別人（其他使用者，或 OwnerId 為 null 的系統品類）占用的那些。
    ///
    /// 封存檔刻意保留原始 ObjectId，但 _id 在 collection 內是全域唯一而不分 owner：
    /// 同一個部署上的另一個帳號可能已經占用了這些 id，直接插入會擲 DuplicateKey。
    /// 自己的品類在匯入過程中一律先刪再寫，所以此查詢刻意只問「別人的」。
    /// </summary>
    Task<IReadOnlyList<ObjectId>> ListCategoryIdsOwnedByOthersAsync(
        IReadOnlyList<ObjectId> ids, CancellationToken ct);

    /// <summary>
    /// 傳入的 id 當中仍存在於 items collection 的那些，不分 owner。
    ///
    /// 必須在 <see cref="DeleteNonSteamItemsAsync"/> 之後呼叫：屆時還留著的，
    /// 若非其他使用者的品項，就是自己被保留的 Steam 品項，兩者都不能被覆蓋。
    /// </summary>
    Task<IReadOnlyList<ObjectId>> ListExistingItemIdsAsync(
        IReadOnlyList<ObjectId> ids, CancellationToken ct);

    /// <summary>
    /// 把仍掛在 <paramref name="fromCategoryId"/> 底下的品項改指到 <paramref name="toCategoryId"/>。
    ///
    /// 以來源品類過濾而非收一份呼叫端事先讀好的 id 清單：這裡沒有 transaction，
    /// 那種清單必然是舊快照，Steam 同步若在讀取與寫入之間插入新品項就會被漏掉，
    /// 接著來源品類被刪除，那筆品項就指向一個不存在的品類。
    /// </summary>
    Task RepointItemsAsync(ObjectId fromCategoryId, ObjectId toCategoryId, CancellationToken ct);

    Task InsertCategoriesAsync(IReadOnlyList<Category> categories, CancellationToken ct);

    Task InsertItemsAsync(IReadOnlyList<Item> items, CancellationToken ct);

    Task InsertShareLinksAsync(IReadOnlyList<ShareLink> links, CancellationToken ct);

    /// <summary>slug 全域唯一，此查詢刻意不套 ownerId 過濾。</summary>
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct);
}
