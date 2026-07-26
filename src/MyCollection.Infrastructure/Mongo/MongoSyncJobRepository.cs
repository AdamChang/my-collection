using MongoDB.Driver;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoSyncJobRepository(MongoContext context, IUserContext userContext) : ISyncJobRepository
{
    private IMongoCollection<SyncJob> Jobs => context.SyncJobs;

    public Task InsertAsync(SyncJob job, CancellationToken ct)
    {
        job.OwnerId = userContext.UserId;
        return Jobs.InsertOneAsync(job, cancellationToken: ct);
    }

    public Task UpdateAsync(SyncJob job, CancellationToken ct) =>
        Jobs.ReplaceOneAsync(
            Builders<SyncJob>.Filter.And(
                Builders<SyncJob>.Filter.Eq(x => x.Id, job.Id),
                Builders<SyncJob>.Filter.Eq(x => x.OwnerId, userContext.UserId)),
            job,
            cancellationToken: ct);

    public async Task<IReadOnlyList<SyncJob>> ListRecentAsync(int limit, CancellationToken ct) =>
        await Jobs
            .Find(Builders<SyncJob>.Filter.Eq(x => x.OwnerId, userContext.UserId))
            .SortByDescending(x => x.StartedAt)
            .Limit(limit)
            .ToListAsync(ct);
}
