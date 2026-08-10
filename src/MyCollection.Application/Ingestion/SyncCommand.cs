using MediatR;
using MongoDB.Bson;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Ingestion;

public record SyncCommand(string Provider) : IRequest<SyncJobDto>;

public record SyncJobDto(
    string Id,
    string Provider,
    string Status,
    int Created,
    int Updated,
    int Failed,
    int Skipped,
    string? Error,
    DateTime StartedAt,
    DateTime? FinishedAt);

public record ListSyncJobsQuery(int Limit = 20) : IRequest<IReadOnlyList<SyncJobDto>>;

public record RetrySyncJobCommand(string JobId) : IRequest<SyncJobDto>;

public static class SyncJobMapper
{
    public static SyncJobDto ToDto(SyncJob job) => new(
        job.Id.ToString(),
        job.Provider,
        job.Status.ToString(),
        job.Created,
        job.Updated,
        job.Failed,
        job.Skipped,
        job.Error,
        job.StartedAt,
        job.FinishedAt);
}

public sealed class SyncCommandHandler(
    ProviderRegistry registry,
    IExternalAccountRepository accounts,
    ISyncJobRepository jobs,
    IIngestionTaskDispatcher dispatcher,
    SyncJobRunner runner,
    TimeProvider timeProvider) : IRequestHandler<SyncCommand, SyncJobDto>
{
    public async Task<SyncJobDto> Handle(SyncCommand request, CancellationToken cancellationToken)
    {
        var provider = registry.Require<IBulkSyncProvider>(request.Provider);

        _ = await accounts.GetAsync(provider.Key, cancellationToken)
            ?? throw new NotFoundException("ExternalAccount", provider.Key);

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var job = new SyncJob
        {
            Id = ObjectId.GenerateNewId(),
            Provider = provider.Key,
            Kind = SyncJobKind.Sync,
            Status = SyncStatus.Running,
            StartedAt = now
        };
        await jobs.InsertAsync(job, cancellationToken);

        if (dispatcher.IsDurable)
        {
            await IngestionTaskDispatch.PersistedAsync(
                dispatcher, jobs, job, timeProvider, cancellationToken);
            return SyncJobMapper.ToDto(job);
        }

        return SyncJobMapper.ToDto(await runner.RunAsync(job, cancellationToken));
    }
}

public sealed class ListSyncJobsQueryHandler(ISyncJobRepository jobs)
    : IRequestHandler<ListSyncJobsQuery, IReadOnlyList<SyncJobDto>>
{
    public async Task<IReadOnlyList<SyncJobDto>> Handle(ListSyncJobsQuery request, CancellationToken cancellationToken)
    {
        var result = await jobs.ListRecentAsync(Math.Clamp(request.Limit, 1, 100), cancellationToken);

        return result.Select(SyncJobMapper.ToDto).ToArray();
    }
}

public sealed class RetrySyncJobCommandHandler(
    ISyncJobRepository jobs,
    IIngestionTaskDispatcher dispatcher,
    TimeProvider timeProvider) : IRequestHandler<RetrySyncJobCommand, SyncJobDto>
{
    public async Task<SyncJobDto> Handle(RetrySyncJobCommand request, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(request.JobId, out var id))
        {
            throw new NotFoundException(nameof(SyncJob), request.JobId);
        }

        var previous = await jobs.GetAsync(id, cancellationToken)
                       ?? throw new NotFoundException(nameof(SyncJob), request.JobId);
        if (previous.Status != SyncStatus.Failed)
        {
            throw new ConflictException("Only failed sync jobs can be retried.");
        }

        var retry = new SyncJob
        {
            Id = ObjectId.GenerateNewId(),
            Provider = previous.Provider,
            Kind = previous.Kind,
            ItemIds = previous.ItemIds is null ? null : [.. previous.ItemIds],
            Limit = previous.Limit,
            Status = SyncStatus.Running,
            StartedAt = timeProvider.GetUtcNow().UtcDateTime
        };

        await jobs.InsertAsync(retry, cancellationToken);
        await IngestionTaskDispatch.PersistedAsync(
            dispatcher, jobs, retry, timeProvider, cancellationToken);
        return SyncJobMapper.ToDto(retry);
    }
}
