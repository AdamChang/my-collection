using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Transfer;

/// <summary>
/// 封存檔的 manifest。刻意不含 ownerId：它由各機器註冊時各自產生，
/// 帶進封存檔只會誤導，匯入端一律改用當前登入者的 id。
/// </summary>
public sealed class ArchiveManifest
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>zip 內 manifest 的固定檔名。</summary>
    public const string FileName = "manifest.json";

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DateTime ExportedAt { get; set; }

    public List<ArchiveCategory> Categories { get; set; } = [];
    public List<ArchiveItem> Items { get; set; } = [];
    public List<ArchiveShareLink> ShareLinks { get; set; } = [];
}

public sealed class ArchiveCategory
{
    public ObjectId Id { get; set; }
    public required string Name { get; set; }
    public string Icon { get; set; } = "box";
    public CategoryKind Kind { get; set; }
    public List<CategoryField> Fields { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ArchiveImage
{
    public required string Id { get; set; }
    public bool IsPrimary { get; set; }
    public int Order { get; set; }

    /// <summary>zip 內的相對路徑，格式為 media/{itemId}/{imageId}.webp，僅 full 尺寸。</summary>
    public required string File { get; set; }
}

public sealed class ArchiveItem
{
    public ObjectId Id { get; set; }
    public ObjectId CategoryId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = [];
    public bool IsShowcased { get; set; }
    public ItemSource Source { get; set; }
    public Acquisition? Acquisition { get; set; }
    public BsonDocument Attributes { get; set; } = [];
    public List<ArchiveImage> Images { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ArchiveShareLink
{
    public required string Slug { get; set; }
    public ShareScope Scope { get; set; }
    public List<ObjectId> IncludeCategoryIds { get; set; } = [];
    public bool IncludePrice { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>zip 內的媒體路徑組裝，匯出與匯入必須用同一份規則。</summary>
public static class ArchivePaths
{
    public static string Image(ObjectId itemId, string imageId) => $"media/{itemId}/{imageId}.webp";
}
