using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Ingestion;

/// <summary>
/// 已建立、等待背景執行的補完作業。
/// 直接帶著 SyncJob 實體而不是只帶 Id：佇列是行程內的，省一次讀取，
/// 也讓背景端不需要一支「依 Id 取作業」的倉儲方法。
/// </summary>
public record EnrichJobRequest(
    SyncJob Job,
    ObjectId OwnerId,
    string Provider,
    IReadOnlyList<string>? ItemIds,
    int Limit);

/// <summary>
/// 行程內佇列。作業狀態存在資料庫，佇列只負責交棒——
/// 重啟會丟掉未執行的項目，使用者重按一次即可，代價低於引入外部佇列。
/// </summary>
public interface IEnrichJobQueue
{
    void Enqueue(EnrichJobRequest request);

    ValueTask<EnrichJobRequest> DequeueAsync(CancellationToken ct);
}
