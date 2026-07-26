using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Ingestion;

public record SyncOutcome(int Created, int Updated, int Failed);

public interface IItemSyncWriter
{
    /// <summary>
    /// 以 (ownerId, provider, externalId) 為鍵做 bulk upsert。
    /// $set 只寫 provider 擁有的欄位，$setOnInsert 保護使用者手動編輯的欄位。
    /// 刻意接受明確的 ownerId：同步是背景作業，不一定有 HTTP 請求脈絡。
    /// </summary>
    Task<SyncOutcome> UpsertAsync(
        ObjectId ownerId,
        ObjectId categoryId,
        ItemSource source,
        string providerKey,
        IReadOnlyList<ExternalItem> items,
        DateTime syncedAt,
        CancellationToken ct);
}
