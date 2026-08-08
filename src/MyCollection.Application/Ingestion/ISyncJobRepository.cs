using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Ingestion;

public interface ISyncJobRepository
{
    Task InsertAsync(SyncJob job, CancellationToken ct);

    Task UpdateAsync(SyncJob job, CancellationToken ct);

    Task<SyncJob?> GetAsync(ObjectId id, CancellationToken ct);

    Task<IReadOnlyList<SyncJob>> ListRecentAsync(int limit, CancellationToken ct);
}

/// <summary>僅供受 OIDC 保護的背景執行器使用；刻意與 owner-scoped repository 分離。</summary>
public interface IBackgroundSyncJobRepository
{
    Task<SyncJob?> ClaimAsync(ObjectId id, DateTime now, DateTime leaseUntil, CancellationToken ct);

    Task<SyncJob?> GetUnscopedAsync(ObjectId id, CancellationToken ct);

    Task ResetForRetryAsync(ObjectId id, CancellationToken ct);
}
