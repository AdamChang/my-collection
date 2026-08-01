using MongoDB.Bson;

namespace MyCollection.Application.Ingestion;

/// <summary>
/// 單一品項要套用的 provider 欄位。Description 為 null 代表不動——
/// 「僅在目前為空時寫入」的判斷由 handler 做完，寫入器不讀取現有文件。
/// </summary>
public record ItemEnrichment(
    ObjectId ItemId,
    string? Description,
    IReadOnlyDictionary<string, object?> Attributes);

public interface IItemEnrichWriter
{
    /// <summary>
    /// 對既有品項套用 provider 欄位，回傳實際命中的筆數。
    /// 只 $set 傳入的 attributes 與非 null 的 description；
    /// name / tags / isShowcased / acquisition / images / createdAt / source 一律不碰。
    /// 絕不 upsert：補完只更新，不建立。
    /// </summary>
    Task<int> ApplyAsync(
        ObjectId ownerId,
        IReadOnlyList<ItemEnrichment> enrichments,
        DateTime enrichedAt,
        string providerKey,
        CancellationToken ct);
}
