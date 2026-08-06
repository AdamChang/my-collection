using MyCollection.Application.Common;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Items;

public record ItemImageDto(string Id, string Path, string CardPath, string ThumbPath, bool IsPrimary, int Order);

public record ExternalRefDto(string Provider, string ExternalId, string? Url, DateTime LastSyncedAt);

public record MoneyDto(decimal Amount, string Currency);

public record AcquisitionDto(DateTime? AcquiredAt, MoneyDto? Price, string? Vendor);

public record ItemDto(
    string Id,
    string CategoryId,
    string Name,
    string? Description,
    IReadOnlyList<ItemImageDto> Images,
    IReadOnlyList<string> Tags,
    bool IsShowcased,
    string Source,
    ExternalRefDto? ExternalRef,
    AcquisitionDto? Acquisition,
    string? LocationId,
    IReadOnlyDictionary<string, object?> Attributes,
    string? DisplayMode,
    int? Rating,
    string? StorageLocation,
    string EffectiveDisplayMode,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public static class ItemMapper
{
    /// <summary>
    /// categoryDefaultDisplayMode 是所屬品類的 DefaultDisplayMode，用來在 item.DisplayMode 為 null
    /// 時算出 EffectiveDisplayMode。呼叫端必須自己載入品類——這裡不查資料庫。
    /// </summary>
    public static ItemDto ToDto(Item item, DisplayMode categoryDefaultDisplayMode) => new(
        item.Id.ToString(),
        item.CategoryId.ToString(),
        item.Name,
        item.Description,
        item.Images.Select(i => new ItemImageDto(i.Id, i.Path, i.CardPath, i.ThumbPath, i.IsPrimary, i.Order)).ToArray(),
        item.Tags,
        item.IsShowcased,
        item.Source.ToString(),
        item.ExternalRef is null
            ? null
            : new ExternalRefDto(item.ExternalRef.Provider, item.ExternalRef.ExternalId, item.ExternalRef.Url, item.ExternalRef.LastSyncedAt),
        item.Acquisition is null
            ? null
            : new AcquisitionDto(
                item.Acquisition.AcquiredAt,
                item.Acquisition.Price is null ? null : new MoneyDto(item.Acquisition.Price.Amount, item.Acquisition.Price.Currency),
                item.Acquisition.Vendor),
        item.LocationId?.ToString(),
        BsonJson.ToDictionary(item.Attributes),
        item.DisplayMode?.ToString(),
        item.Rating,
        item.StorageLocation,
        (item.DisplayMode ?? categoryDefaultDisplayMode).ToString(),
        item.CreatedAt,
        item.UpdatedAt);
}
