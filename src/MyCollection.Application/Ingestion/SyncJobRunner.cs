using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Ingestion;

public sealed class SyncJobRunner(
    ProviderRegistry registry,
    IExternalAccountRepository accounts,
    ISyncJobRepository jobs,
    IItemSyncWriter writer,
    ICategoryRepository categories,
    IUserContext userContext,
    TimeProvider timeProvider)
{
    private const string DigitalCategoryName = "數位遊戲";

    public async Task<SyncJob> RunAsync(SyncJob job, CancellationToken ct)
    {
        try
        {
            var provider = registry.Require<IBulkSyncProvider>(job.Provider);
            var account = await accounts.GetAsync(provider.Key, ct)
                          ?? throw new NotFoundException("ExternalAccount", provider.Key);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var externalItems = await provider.SyncAsync(account, ct);
            var category = await GetDigitalCategoryAsync(ct);
            var outcome = await writer.UpsertAsync(
                userContext.UserId,
                category.Id,
                ToSource(provider.Key),
                provider.Key,
                externalItems,
                now,
                ct);

            job.Created = outcome.Created;
            job.Updated = outcome.Updated;
            job.Failed = outcome.Failed;
            job.Status = SyncStatus.Succeeded;
            job.Error = null;
        }
        catch (Exception exception)
        {
            job.Status = SyncStatus.Failed;
            job.Error = exception.Message;
            job.FinishedAt = timeProvider.GetUtcNow().UtcDateTime;
            job.LeaseUntil = null;
            await jobs.UpdateAsync(job, ct);
            throw;
        }

        job.FinishedAt = timeProvider.GetUtcNow().UtcDateTime;
        job.LeaseUntil = null;
        await jobs.UpdateAsync(job, ct);
        return job;
    }

    private async Task<Category> GetDigitalCategoryAsync(CancellationToken ct)
    {
        var existing = (await categories.ListAsync(ct))
            .Where(category => string.Equals(category.Name, DigitalCategoryName, StringComparison.Ordinal))
            .OrderBy(category => category.OwnerId is null)
            .FirstOrDefault();
        return existing ?? throw new NotFoundException("Category", DigitalCategoryName);
    }

    private static ItemSource ToSource(string providerKey) =>
        Enum.TryParse<ItemSource>(providerKey, ignoreCase: true, out var source) ? source : ItemSource.Manual;
}
