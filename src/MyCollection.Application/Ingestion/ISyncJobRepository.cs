using MyCollection.Domain.Entities;

namespace MyCollection.Application.Ingestion;

public interface ISyncJobRepository
{
    Task InsertAsync(SyncJob job, CancellationToken ct);

    Task UpdateAsync(SyncJob job, CancellationToken ct);

    Task<IReadOnlyList<SyncJob>> ListRecentAsync(int limit, CancellationToken ct);
}
