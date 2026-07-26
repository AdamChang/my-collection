using MongoDB.Bson;

namespace MyCollection.Application.Showcase;

/// <summary>
/// Showcase 圖片延遲下載佇列。同步時不下載 300 張圖，只有被設為精選的品項
/// 才把 provider 的遠端圖片抓回本地儲存。
/// </summary>
public interface IShowcaseImageQueue
{
    void Enqueue(ObjectId itemId);

    ValueTask<ObjectId> DequeueAsync(CancellationToken ct);
}
