using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
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
    string? Error,
    DateTime StartedAt,
    DateTime? FinishedAt);

public record ListSyncJobsQuery(int Limit = 20) : IRequest<IReadOnlyList<SyncJobDto>>;

public static class SyncJobMapper
{
    public static SyncJobDto ToDto(SyncJob job) => new(
        job.Id.ToString(),
        job.Provider,
        job.Status.ToString(),
        job.Created,
        job.Updated,
        job.Failed,
        job.Error,
        job.StartedAt,
        job.FinishedAt);
}

public sealed class SyncCommandHandler(
    ProviderRegistry registry,
    IExternalAccountRepository accounts,
    ISyncJobRepository jobs,
    IItemSyncWriter writer,
    ICategoryRepository categories,
    IUserContext userContext,
    TimeProvider timeProvider) : IRequestHandler<SyncCommand, SyncJobDto>
{
    /// <summary>數位品項同步的目標品類；不存在時自動建立。</summary>
    private const string DigitalCategoryName = "數位遊戲";

    public async Task<SyncJobDto> Handle(SyncCommand request, CancellationToken cancellationToken)
    {
        var provider = registry.Require(request.Provider, ProviderCapability.BulkSync);

        var account = await accounts.GetAsync(provider.Key, cancellationToken)
                      ?? throw new NotFoundException("ExternalAccount", provider.Key);

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var job = new SyncJob
        {
            Id = ObjectId.GenerateNewId(),
            Provider = provider.Key,
            Status = SyncStatus.Running,
            StartedAt = now
        };
        await jobs.InsertAsync(job, cancellationToken);

        try
        {
            var externalItems = await provider.SyncAsync(account, cancellationToken);
            var category = await EnsureDigitalCategoryAsync(now, cancellationToken);

            var outcome = await writer.UpsertAsync(
                userContext.UserId,
                category.Id,
                ToSource(provider.Key),
                provider.Key,
                externalItems,
                now,
                cancellationToken);

            job.Created = outcome.Created;
            job.Updated = outcome.Updated;
            job.Failed = outcome.Failed;
            job.Status = SyncStatus.Succeeded;
        }
        catch (Exception ex)
        {
            // 同步失敗不中斷使用者流程：記錄後由 UI 顯示並提供重試
            job.Status = SyncStatus.Failed;
            job.Error = ex.Message;
            job.FinishedAt = timeProvider.GetUtcNow().UtcDateTime;
            await jobs.UpdateAsync(job, cancellationToken);
            throw;
        }

        job.FinishedAt = timeProvider.GetUtcNow().UtcDateTime;
        await jobs.UpdateAsync(job, cancellationToken);

        return SyncJobMapper.ToDto(job);
    }

    private async Task<Category> EnsureDigitalCategoryAsync(DateTime now, CancellationToken ct)
    {
        var existing = await categories.FindByNameAsync(DigitalCategoryName, ct);
        if (existing is not null)
        {
            return existing;
        }

        var category = new Category
        {
            Id = ObjectId.GenerateNewId(),
            Name = DigitalCategoryName,
            Icon = "game",
            Kind = CategoryKind.Digital,
            Fields = [],
            CreatedAt = now,
            UpdatedAt = now
        };

        await categories.InsertAsync(category, ct);

        return category;
    }

    private static ItemSource ToSource(string providerKey) =>
        Enum.TryParse<ItemSource>(providerKey, ignoreCase: true, out var source) ? source : ItemSource.Manual;
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
