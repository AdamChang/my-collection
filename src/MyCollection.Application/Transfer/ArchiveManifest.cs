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

/// <summary>封存檔無法解析或版本不支援。由匯入端轉成 400。</summary>
public sealed class InvalidArchiveException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class ArchiveCategory
{
    public ObjectId Id { get; set; }
    public required string Name { get; set; }
    public string Icon { get; set; } = "box";
    public CategoryKind Kind { get; set; }
    public List<ArchiveCategoryField> Fields { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 刻意與 Domain 的 <see cref="CategoryField"/> 分開：這是磁碟格式，
/// Domain 型別改了欄位不該悄悄改動舊封存檔的讀法，兩者的落差交由 SchemaVersion 仲裁。
/// </summary>
public sealed class ArchiveCategoryField
{
    public required string Key { get; set; }
    public required string Label { get; set; }
    public FieldType Type { get; set; }
    public List<string>? Options { get; set; }
    public bool Required { get; set; }
    public bool Searchable { get; set; }
    public bool ShowOnCard { get; set; }
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
    public ArchiveAcquisition? Acquisition { get; set; }
    public BsonDocument Attributes { get; set; } = [];
    public List<ArchiveImage> Images { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>磁碟格式版的 <see cref="Acquisition"/>，見 <see cref="ArchiveCategoryField"/> 上的說明。</summary>
public sealed class ArchiveAcquisition
{
    public DateTime? AcquiredAt { get; set; }
    public ArchiveMoney? Price { get; set; }
    public string? Vendor { get; set; }
}

/// <summary>磁碟格式版的 <see cref="Money"/>。</summary>
public sealed class ArchiveMoney
{
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
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
