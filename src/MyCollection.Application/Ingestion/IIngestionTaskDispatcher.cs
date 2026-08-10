using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Ingestion;

public interface IIngestionTaskDispatcher
{
    bool IsDurable { get; }

    Task DispatchAsync(ObjectId operationId, CancellationToken ct);
}

public interface ICloudTaskAuthenticator
{
    Task<bool> IsAuthorizedAsync(string? authorizationHeader, CancellationToken ct);
}

public enum IngestionExecutionResult
{
    Succeeded,
    AlreadyCompleted,
    Busy,
    FailedTerminal,
    NotFound
}

internal static class IngestionTaskDispatch
{
    public static async Task PersistedAsync(
        IIngestionTaskDispatcher dispatcher,
        ISyncJobRepository jobs,
        SyncJob job,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        try
        {
            await dispatcher.DispatchAsync(job.Id, ct);
        }
        catch (Exception exception)
        {
            job.Status = SyncStatus.Failed;
            job.Error = $"Task dispatch failed: {exception.Message}";
            job.FinishedAt = timeProvider.GetUtcNow().UtcDateTime;
            await jobs.UpdateAsync(job, ct);
            throw;
        }
    }
}
