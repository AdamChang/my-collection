namespace MyCollection.Application.Sharing;

public record ShareLinkDto(
    string Id,
    string Slug,
    string Scope,
    IReadOnlyList<string> IncludeCategoryIds,
    bool IncludePrice,
    DateTime? ExpiresAt,
    DateTime CreatedAt);

public record PublicImageDto(string CardPath, string ThumbPath, bool IsPrimary, int Order);

public record PublicPriceDto(decimal Amount, string Currency);

/// <summary>
/// 公開分享頁專用 DTO。刻意不共用 ItemDto——內部 DTO 新增欄位時不可能意外洩漏。
/// </summary>
public record PublicItemDto(
    string Id,
    string Name,
    string? Description,
    string CategoryName,
    IReadOnlyList<string> Tags,
    IReadOnlyList<PublicImageDto> Images,
    IReadOnlyDictionary<string, object?> Attributes,
    PublicPriceDto? Price);

public record PublicShareDto(
    string OwnerDisplayName,
    string Scope,
    IReadOnlyList<PublicItemDto> Items);
