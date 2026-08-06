using MongoDB.Bson;
using MyCollection.Application.Categories;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Sharing;

/// <summary>
/// 公開分享頁專用的投影結果。刻意不含 Acquisition 的 Vendor 與 Item.StorageLocation——後者永不投影，
/// 沒有對應的旗標。Price／AcquiredAt 只有在 ShareLink.IncludePrice 為 true 時才被投影出來，
/// Rating 只有在 ShareLink.IncludeRating 為 true 時才被投影出來。DisplayMode 永遠投影，
/// 用來算 EffectiveDisplayMode，不受任何旗標控制。
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

    /// <summary>品項層級的展示模式覆寫。null = 沿用所屬品類的 DefaultDisplayMode。</summary>
    public DisplayMode? DisplayMode { get; set; }

    public Money? Price { get; set; }
    public DateTime? AcquiredAt { get; set; }
    public int? Rating { get; set; }
}

/// <summary>品類名稱＋精選牆需要的展示中繼資料。CardFields 只含 ShowOnCard 的欄位。</summary>
public sealed record PublicCategoryInfo(string Name, DisplayMode DefaultDisplayMode, IReadOnlyList<CategoryFieldDto> CardFields);

public interface IPublicCatalogReader
{
    /// <summary>刻意接受明確的 ownerId：這條路徑不經過 IUserContext（呼叫端是匿名的）。</summary>
    Task<IReadOnlyList<PublicItemProjection>> ListItemsAsync(
        ObjectId ownerId,
        ShareScope scope,
        IReadOnlyList<ObjectId> categoryIds,
        bool includePrice,
        bool includeRating,
        CancellationToken ct);

    Task<IReadOnlyDictionary<ObjectId, PublicCategoryInfo>> ListCategoriesAsync(ObjectId ownerId, CancellationToken ct);
}
