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
    DateTime CreatedAt,
    DateTime UpdatedAt);

public static class ItemMapper
{
    public static ItemDto ToDto(Item item) => new(
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
        item.CreatedAt,
        item.UpdatedAt);
}
