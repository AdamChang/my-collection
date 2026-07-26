using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Sharing;

/// <summary>
/// 公開分享頁專用的投影結果。刻意不含 Acquisition 的 AcquiredAt 與 Vendor，
/// Price 只有在 ShareLink.IncludePrice 為 true 時才被投影出來。
/// </summary>
public sealed class PublicItemProjection
{
    public ObjectId Id { get; set; }
    public ObjectId CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<ItemImage> Images { get; set; } = [];
    public BsonDocument Attributes { get; set; } = [];
    public Money? Price { get; set; }
}

public interface IPublicCatalogReader
{
    /// <summary>刻意接受明確的 ownerId：這條路徑不經過 IUserContext（呼叫端是匿名的）。</summary>
    Task<IReadOnlyList<PublicItemProjection>> ListItemsAsync(
        ObjectId ownerId,
        ShareScope scope,
        IReadOnlyList<ObjectId> categoryIds,
        bool includePrice,
        CancellationToken ct);

    Task<IReadOnlyDictionary<ObjectId, string>> ListCategoryNamesAsync(ObjectId ownerId, CancellationToken ct);
}
