using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Transfer;

/// <summary>
/// Domain 實體與封存檔型別之間的唯一對應點。
///
/// 兩個方向放在一起是刻意的：欄位對應寫錯的後果是資料悄悄遺失，
/// 而不是編譯失敗。放在同一個檔案裡，加欄位時漏掉另一邊會立刻看得出來。
/// </summary>
public static class ArchiveMapper
{
    // ---- Domain → 封存檔 ----

    public static ArchiveCategory ToArchive(Category category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        Icon = category.Icon,
        Kind = category.Kind,
        Fields = [.. category.Fields.Select(ToArchive)],
        CreatedAt = category.CreatedAt,
        UpdatedAt = category.UpdatedAt
    };

    public static ArchiveCategoryField ToArchive(CategoryField field) => new()
    {
        Key = field.Key,
        Label = field.Label,
        Type = field.Type,
        Options = field.Options,
        Required = field.Required,
        Searchable = field.Searchable,
        ShowOnCard = field.ShowOnCard
    };

    public static ArchiveAcquisition? ToArchive(Acquisition? acquisition) => acquisition is null
        ? null
        : new ArchiveAcquisition
        {
            AcquiredAt = acquisition.AcquiredAt,
            Vendor = acquisition.Vendor,
            Price = acquisition.Price is null
                ? null
                : new ArchiveMoney { Amount = acquisition.Price.Amount, Currency = acquisition.Price.Currency }
        };

    public static ArchiveItem ToArchive(Item item) => new()
    {
        Id = item.Id,
        CategoryId = item.CategoryId,
        Name = item.Name,
        Description = item.Description,
        Tags = item.Tags,
        IsShowcased = item.IsShowcased,
        Source = item.Source,
        Acquisition = ToArchive(item.Acquisition),
        Attributes = item.Attributes,
        Images =
        [
            .. item.Images.Select(image => new ArchiveImage
            {
                Id = image.Id,
                IsPrimary = image.IsPrimary,
                Order = image.Order,
                File = ArchivePaths.Image(item.Id, image.Id)
            })
        ],
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt
    };

    public static ArchiveShareLink ToArchive(ShareLink link) => new()
    {
        Slug = link.Slug,
        Scope = link.Scope,
        IncludeCategoryIds = link.IncludeCategoryIds,
        IncludePrice = link.IncludePrice,
        ExpiresAt = link.ExpiresAt,
        CreatedAt = link.CreatedAt
    };

    // ---- 封存檔 → Domain ----

    /// <param name="ownerId">
    /// 封存檔不帶 ownerId，一律由呼叫端指定。驗證階段只需要 schema，可傳 null。
    /// </param>
    public static Category ToDomain(ArchiveCategory source, ObjectId? ownerId) => new()
    {
        Id = source.Id,
        OwnerId = ownerId,
        Name = source.Name,
        Icon = source.Icon,
        Kind = source.Kind,
        Fields = [.. source.Fields.Select(ToDomain)],
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt
    };

    public static CategoryField ToDomain(ArchiveCategoryField source) => new()
    {
        Key = source.Key,
        Label = source.Label,
        Type = source.Type,
        Options = source.Options,
        Required = source.Required,
        Searchable = source.Searchable,
        ShowOnCard = source.ShowOnCard
    };

    public static Acquisition? ToDomain(ArchiveAcquisition? source) => source is null
        ? null
        : new Acquisition
        {
            AcquiredAt = source.AcquiredAt,
            Vendor = source.Vendor,
            Price = source.Price is null ? null : new Money(source.Price.Amount, source.Price.Currency)
        };
}
