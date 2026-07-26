using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Items;

/// <summary>Repository 層的查詢條件。ownerId 不在此，由 Repository 自 IUserContext 強制加上。</summary>
public sealed class ItemQuerySpec
{
    public string? Search { get; init; }
    public ObjectId? CategoryId { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public bool? IsShowcased { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 24;

    /// <summary>依 category schema 的 searchable 欄位篩選，key 為 field key、value 為精確比對值。</summary>
    public IReadOnlyDictionary<string, string>? Attributes { get; init; }
}

public interface IItemRepository
{
    Task<Item?> GetAsync(ObjectId id, CancellationToken ct);

    Task<PagedResult<Item>> SearchAsync(ItemQuerySpec spec, CancellationToken ct);

    Task<IReadOnlyList<string>> ListTagsAsync(CancellationToken ct);

    Task InsertAsync(Item item, CancellationToken ct);

    /// <summary>找不到（含不屬於自己）擲 NotFoundException。</summary>
    Task UpdateAsync(Item item, CancellationToken ct);

    Task DeleteAsync(ObjectId id, CancellationToken ct);
}
