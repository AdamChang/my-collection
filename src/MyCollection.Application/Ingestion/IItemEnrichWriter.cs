using MongoDB.Bson;

namespace MyCollection.Application.Ingestion;

/// <summary>
/// 單一品項要套用的 provider 欄位。Name / Description 為 null 代表不動——
/// 欄位擁有權（覆蓋或僅在缺值時寫入）的判斷由 handler 做完，寫入器不讀取現有文件。
/// </summary>
public record ItemEnrichment(
    ObjectId ItemId,
    string? Name,
    string? Description,
    IReadOnlyDictionary<string, object?> Attributes);

public interface IItemEnrichWriter
{
    /// <summary>
    /// 對既有品項套用 provider 欄位，回傳實際命中的筆數。
    /// 只 $set 傳入的 attributes 與非 null 的 name / description；
    /// tags / isShowcased / acquisition / images / createdAt / source 一律不碰。
    /// 絕不 upsert：補完只更新，不建立。
    ///
    /// name 是刻意放寬的：繁體中文品名由 Steam 商店補完提供，而同步只拿得到英文，
    /// 所以 name 的擁有者從同步移到補完（見 MongoItemSyncWriter 的 $setOnInsert）。
    /// </summary>
    Task<int> ApplyAsync(
        ObjectId ownerId,
        IReadOnlyList<ItemEnrichment> enrichments,
        DateTime enrichedAt,
        string providerKey,
        CancellationToken ct);
}
