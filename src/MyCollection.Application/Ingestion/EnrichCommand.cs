using FluentValidation;
using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Ingestion;

/// <summary>
/// 補完既有品項的 provider 欄位。給 ItemIds 是單筆／重抓，不給是批次補完尚未綁定的品項。
/// </summary>
public record EnrichCommand(
    string Provider,
    IReadOnlyList<string>? ItemIds = null,
    int Limit = 50) : IRequest<SyncJobDto>;

public sealed class EnrichCommandValidator : AbstractValidator<EnrichCommand>
{
    public EnrichCommandValidator()
    {
        RuleFor(x => x.Provider).NotEmpty();

        RuleForEach(x => x.ItemIds)
            .Must(id => ObjectId.TryParse(id, out _))
            .WithMessage("ItemIds must contain valid object ids.");
    }
}

public sealed class EnrichCommandHandler(
    ProviderRegistry registry,
    IItemRepository items,
    ICategoryRepository categories,
    ISyncJobRepository jobs,
    IItemEnrichWriter writer,
    IUserContext userContext,
    TimeProvider timeProvider) : IRequestHandler<EnrichCommand, SyncJobDto>
{
    public async Task<SyncJobDto> Handle(EnrichCommand request, CancellationToken cancellationToken)
    {
        var provider = registry.Require<ISearchProvider>(request.Provider);
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
            var targets = await LoadTargetsAsync(request, provider, cancellationToken);

            // 沒有可用外部識別碼的品項不猜，直接記為 skipped
            var addressable = targets.Where(t => t.ExternalId is not null).ToArray();
            job.Skipped = targets.Length - addressable.Length;

            if (addressable.Length > 0)
            {
                var lookup = await provider.FetchByExternalIdsAsync(
                    addressable.Select(t => t.ExternalId!).Distinct(StringComparer.Ordinal).ToArray(),
                    cancellationToken);

                var failedIds = lookup.FailedIds.ToHashSet(StringComparer.Ordinal);
                var allowedKeys = await AllowedKeysByCategoryAsync(addressable, cancellationToken);

                var enrichments = new List<ItemEnrichment>();

                foreach (var target in addressable)
                {
                    if (failedIds.Contains(target.ExternalId!))
                    {
                        job.Failed++;
                    }
                    else if (lookup.Found.TryGetValue(target.ExternalId!, out var source))
                    {
                        enrichments.Add(ToEnrichment(target.Item, source, allowedKeys[target.Item.CategoryId]));
                    }
                    else
                    {
                        // 查無對應不是失敗
                        job.Skipped++;
                    }
                }

                job.Updated = await writer.ApplyAsync(
                    userContext.UserId, enrichments, now, provider.Key, cancellationToken);
            }

            job.Status = SyncStatus.Succeeded;
        }
        catch (Exception ex)
        {
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

    private async Task<EnrichTarget[]> LoadTargetsAsync(
        EnrichCommand request, ISearchProvider provider, CancellationToken ct)
    {
        var loaded = request.ItemIds is { Count: > 0 }
            ? await items.ListByIdsAsync(
                request.ItemIds.Select(ObjectId.Parse).ToArray(), ct)
            : await items.ListEnrichmentCandidatesAsync(
                provider.MarkerAttributeKey, Math.Clamp(request.Limit, 1, 200), ct);

        return loaded.Select(item => new EnrichTarget(item, ExternalIdFor(item, provider))).ToArray();
    }

    /// <summary>
    /// 已綁定過就直接用 marker，不必再繞 Steam 反查一次；
    /// 否則退回外部來源的識別碼。兩者皆無代表這是手動建檔且未綁定的品項——不猜。
    /// </summary>
    private static string? ExternalIdFor(Item item, ISearchProvider provider)
    {
        if (item.Attributes.TryGetValue(provider.MarkerAttributeKey, out var marker)
            && !marker.IsBsonNull)
        {
            return $"{provider.Key}:{marker.ToInt64()}";
        }

        return item.ExternalRef is { } reference
            ? $"{reference.Provider}:{reference.ExternalId}"
            : null;
    }

    private async Task<Dictionary<ObjectId, HashSet<string>>> AllowedKeysByCategoryAsync(
        IReadOnlyList<EnrichTarget> targets, CancellationToken ct)
    {
        var all = await categories.ListAsync(ct);
        var byId = all.ToDictionary(c => c.Id);

        return targets
            .Select(t => t.Item.CategoryId)
            .Distinct()
            .ToDictionary(
                id => id,
                id => byId.TryGetValue(id, out var category)
                    ? category.Fields.Select(f => f.Key).ToHashSet(StringComparer.Ordinal)
                    : []);
    }

    /// <summary>
    /// 品類沒宣告的 key 會被 AttributeValidator 擋掉，讓使用者之後任何一次更新都失敗，
    /// 所以在這裡先濾掉——功能降級，不是中斷。
    /// </summary>
    private static ItemEnrichment ToEnrichment(Item item, ExternalItem source, HashSet<string> allowedKeys)
    {
        var attributes = source.Attributes
            .Where(pair => allowedKeys.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        // 使用者寫過的心得不該被英文簡介蓋掉
        var description = string.IsNullOrWhiteSpace(item.Description) ? source.Description : null;

        return new ItemEnrichment(item.Id, description, attributes);
    }

    private sealed record EnrichTarget(Item Item, string? ExternalId);
}
