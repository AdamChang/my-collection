using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Sharing;

public interface IShareLinkRepository
{
    Task<IReadOnlyList<ShareLink>> ListAsync(CancellationToken ct);

    /// <summary>公開查詢用，刻意不套 ownerId 過濾。</summary>
    Task<ShareLink?> GetBySlugAsync(string slug, CancellationToken ct);

    /// <summary>slug 重複時擲 ConflictException。</summary>
    Task InsertAsync(ShareLink link, CancellationToken ct);

    Task DeleteAsync(ObjectId id, CancellationToken ct);
}
