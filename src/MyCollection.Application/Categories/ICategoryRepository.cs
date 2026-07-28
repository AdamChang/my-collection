using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Categories;

public interface ICategoryRepository
{
    /// <summary>自己的品類 + 系統內建品類（ownerId = null）。</summary>
    Task<IReadOnlyList<Category>> ListAsync(CancellationToken ct);

    /// <summary>非自己也非系統內建時回傳 null。</summary>
    Task<Category?> GetAsync(ObjectId id, CancellationToken ct);

    Task InsertAsync(Category category, CancellationToken ct);

    /// <summary>找不到擲 NotFoundException；系統內建品類擲 ForbiddenException。</summary>
    Task UpdateAsync(Category category, CancellationToken ct);

    Task DeleteAsync(ObjectId id, CancellationToken ct);
}
