using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoSyncJobRepository(MongoContext context, IUserContext userContext)
    : ISyncJobRepository, IBackgroundSyncJobRepository
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

    public Task<SyncJob?> GetAsync(ObjectId id, CancellationToken ct) =>
        Jobs.Find(Builders<SyncJob>.Filter.And(
                Builders<SyncJob>.Filter.Eq(x => x.Id, id),
                Builders<SyncJob>.Filter.Eq(x => x.OwnerId, userContext.UserId)))
            .FirstOrDefaultAsync(ct)!;

    public async Task<IReadOnlyList<SyncJob>> ListRecentAsync(int limit, CancellationToken ct) =>
        await Jobs
            .Find(Builders<SyncJob>.Filter.Eq(x => x.OwnerId, userContext.UserId))
            .SortByDescending(x => x.StartedAt)
            .Limit(limit)
            .ToListAsync(ct);

    public Task<SyncJob?> ClaimAsync(ObjectId id, DateTime now, DateTime leaseUntil, CancellationToken ct)
    {
        var filter = Builders<SyncJob>.Filter.And(
            Builders<SyncJob>.Filter.Eq(x => x.Id, id),
            Builders<SyncJob>.Filter.Eq(x => x.Status, SyncStatus.Running),
            Builders<SyncJob>.Filter.Or(
                Builders<SyncJob>.Filter.Eq(x => x.LeaseUntil, null),
                Builders<SyncJob>.Filter.Lte(x => x.LeaseUntil, now)));
        var update = Builders<SyncJob>.Update
            .Set(x => x.LeaseUntil, leaseUntil)
            .Inc(x => x.Attempt, 1);

        return Jobs.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<SyncJob> { ReturnDocument = ReturnDocument.After },
            ct)!;
    }

    public Task<SyncJob?> GetUnscopedAsync(ObjectId id, CancellationToken ct) =>
        Jobs.Find(Builders<SyncJob>.Filter.Eq(x => x.Id, id)).FirstOrDefaultAsync(ct)!;

    public Task ResetForRetryAsync(ObjectId id, CancellationToken ct) =>
        Jobs.UpdateOneAsync(
            Builders<SyncJob>.Filter.And(
                Builders<SyncJob>.Filter.Eq(x => x.Id, id),
                Builders<SyncJob>.Filter.Eq(x => x.Status, SyncStatus.Failed)),
            Builders<SyncJob>.Update
                .Set(x => x.Status, SyncStatus.Running)
                .Set(x => x.LeaseUntil, null)
                .Set(x => x.Error, null)
                .Set(x => x.FinishedAt, null)
                .Set(x => x.Created, 0)
                .Set(x => x.Updated, 0)
                .Set(x => x.Failed, 0)
                .Set(x => x.Skipped, 0),
            cancellationToken: ct);
}
