using MyCollection.Application.Categories;

namespace MyCollection.Application.Sharing;

public record ShareLinkDto(
    string Id,
    string Slug,
    string Scope,
    IReadOnlyList<string> IncludeCategoryIds,
    bool IncludePrice,
    bool IncludeRating,
    int CollageSlotCount,
    DateTime? ExpiresAt,
    DateTime CreatedAt);

public record PublicImageDto(string CardPath, string ThumbPath, bool IsPrimary, int Order);

public record PublicPriceDto(decimal Amount, string Currency);

/// <summary>
/// 公開分享頁專用 DTO。刻意不共用 ItemDto——內部 DTO 新增欄位時不可能意外洩漏。
/// StorageLocation 刻意不存在於這裡，永不加入（見 ADR-0008）。AcquiredAt 只有 IncludePrice
/// 時才有值，Rating 只有 IncludeRating 時才有值。
/// </summary>
public record PublicItemDto(
    string Id,
    string Name,
    string? Description,
    string CategoryName,
    IReadOnlyList<string> Tags,
    IReadOnlyList<PublicImageDto> Images,
    IReadOnlyDictionary<string, object?> Attributes,
    IReadOnlyList<CategoryFieldDto> CardFields,
    string EffectiveDisplayMode,
    PublicPriceDto? Price,
    DateTime? AcquiredAt,
    int? Rating);

public record PublicShareDto(
    string OwnerDisplayName,
    string Scope,
    int CollageSlotCount,
    IReadOnlyList<PublicItemDto> Items);
