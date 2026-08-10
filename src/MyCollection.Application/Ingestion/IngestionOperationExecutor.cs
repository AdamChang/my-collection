using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Ingestion;

public sealed class IngestionOperationExecutor(
    IBackgroundSyncJobRepository backgroundJobs,
    ISyncJobRepository jobs,
    BackgroundUserContext userContext,
    ProviderRegistry registry,
    SyncJobRunner syncRunner,
    EnrichJobRunner enrichRunner,
    TimeProvider timeProvider)
{
    private const int MaxAttempts = 5;
    // Queue min-backoff 是 10 秒；revision 突然終止時，下一次 delivery 必須能重新 claim，
    // 否則 Busy 回應本身會耗掉有限的五次 attempts。
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(5);

    public async Task<IngestionExecutionResult> ExecuteAsync(ObjectId operationId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var job = await backgroundJobs.ClaimAsync(operationId, now, now.Add(LeaseDuration), ct);
        if (job is null)
        {
            var existing = await backgroundJobs.GetUnscopedAsync(operationId, ct);
            return existing switch
            {
                null => IngestionExecutionResult.NotFound,
                { Status: SyncStatus.Running } => IngestionExecutionResult.Busy,
                _ => IngestionExecutionResult.AlreadyCompleted
            };
        }

        userContext.Set(job.OwnerId);

        try
        {
            switch (job.Kind)
            {
                case SyncJobKind.Sync:
                    await syncRunner.RunAsync(job, ct);
                    break;
                case SyncJobKind.Enrich:
                    await enrichRunner.RunAsync(
                        job,
                        registry.Require<IExternalIdLookupProvider>(job.Provider),
                        job.ItemIds,
                        job.Limit,
                        ct);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported sync job kind '{job.Kind}'.");
            }

            return IngestionExecutionResult.Succeeded;
        }
        catch (Exception exception)
        {
            if (job.Status != SyncStatus.Failed)
            {
                job.Status = SyncStatus.Failed;
                job.Error = exception.Message;
                job.FinishedAt = timeProvider.GetUtcNow().UtcDateTime;
                job.LeaseUntil = null;
                await jobs.UpdateAsync(job, ct);
            }

            if (job.Attempt < MaxAttempts)
            {
                await backgroundJobs.ResetForRetryAsync(operationId, ct);
                throw;
            }

            return IngestionExecutionResult.FailedTerminal;
        }
    }
}
