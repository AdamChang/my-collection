using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCollection.Domain.Entities;

public enum ItemSource
{
    Manual,
    Steam,
    Psn,
    OpenGraph
}

public sealed class ItemImage
{
    /// <summary>圖片在品項內的識別碼，用於 DELETE 路由。</summary>
    public required string Id { get; set; }

    /// <summary>full 尺寸的儲存相對路徑。</summary>
    public required string Path { get; set; }

    public required string CardPath { get; set; }
    public required string ThumbPath { get; set; }

    public bool IsPrimary { get; set; }
    public int Order { get; set; }
}

public sealed class ExternalRef
{
    public required string Provider { get; set; }
    public required string ExternalId { get; set; }
    public string? Url { get; set; }
    public DateTime LastSyncedAt { get; set; }
}

public sealed record Money(decimal Amount, string Currency);

public sealed class Acquisition
{
    public DateTime? AcquiredAt { get; set; }
    public Money? Price { get; set; }
    public string? Vendor { get; set; }
}

public sealed class Item
{
    public ObjectId Id { get; set; }
    public ObjectId OwnerId { get; set; }
    public ObjectId CategoryId { get; set; }

    public required string Name { get; set; }
    public string? Description { get; set; }

    public List<ItemImage> Images { get; set; } = [];
    public List<string> Tags { get; set; } = [];

    public bool IsShowcased { get; set; }
    public ItemSource Source { get; set; } = ItemSource.Manual;
    public ExternalRef? ExternalRef { get; set; }
    public Acquisition? Acquisition { get; set; }

    /// <summary>位置階層第一版不實作，欄位先保留。Digital 品類恆為 null。</summary>
    public ObjectId? LocationId { get; set; }

    /// <summary>精選牆展示版面覆寫。null = 沿用所屬品類的 DefaultDisplayMode。</summary>
    public DisplayMode? DisplayMode { get; set; }

    /// <summary>1–10，不限品類。</summary>
    public int? Rating { get; set; }

    /// <summary>自由文字，如「A櫃-第2層」。永不出現在公開分享頁。</summary>
    public string? StorageLocation { get; set; }

    /// <summary>品類 schema 定義的自訂欄位。BsonDocument 天然支援巢狀結構。</summary>
    [BsonElement("attributes")]
    public BsonDocument Attributes { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
