using MongoDB.Bson;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Ingestion;

/// <summary>
/// 補完作業的實際工作：定址、反查、套用欄位擁有權、寫入、結算作業狀態。
///
/// 與 EnrichCommandHandler 分開，是因為同一段工作有兩種執行方式——
/// IGDB 在請求內跑完，Steam 商店丟背景佇列。把工作抽出來，兩條路徑共用同一份邏輯，
/// 也讓測試能在「工作仍為同步、結果可直接觀察」的最高點驅動它。
/// </summary>
public sealed class EnrichJobRunner(
    IItemRepository items,
    ICategoryRepository categories,
    ISyncJobRepository jobs,
    IItemEnrichWriter writer,
    IUserContext userContext,
    TimeProvider timeProvider)
{
    public async Task<SyncJob> RunAsync(
        SyncJob job,
        IExternalIdLookupProvider provider,
        IReadOnlyList<string>? itemIds,
        int limit,
        CancellationToken ct)
    {
        try
        {
            var targets = await LoadTargetsAsync(provider, itemIds, limit, ct);

            // 沒有可用外部識別碼的品項不猜，直接記為 skipped
            var addressable = targets.Where(t => t.ExternalId is not null).ToArray();
            job.Skipped = targets.Length - addressable.Length;

            if (addressable.Length > 0)
            {
                await ApplyAsync(job, provider, addressable, ct);
            }

            job.Status = SyncStatus.Succeeded;
        }
        catch (Exception ex)
        {
            job.Status = SyncStatus.Failed;
            job.Error = ex.Message;
            job.FinishedAt = timeProvider.GetUtcNow().UtcDateTime;
            await jobs.UpdateAsync(job, ct);
            throw;
        }

        job.FinishedAt = timeProvider.GetUtcNow().UtcDateTime;
        await jobs.UpdateAsync(job, ct);

        return job;
    }

    private async Task ApplyAsync(
        SyncJob job, IExternalIdLookupProvider provider, EnrichTarget[] addressable, CancellationToken ct)
    {
        var lookup = await provider.FetchByExternalIdsAsync(
            addressable.Select(t => t.ExternalId!).Distinct(StringComparer.Ordinal).ToArray(), ct);

        var failedIds = lookup.FailedIds.ToHashSet(StringComparer.Ordinal);
        var allowedKeys = await AllowedKeysByCategoryAsync(addressable, ct);

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
            userContext.UserId, enrichments, timeProvider.GetUtcNow().UtcDateTime, provider.Key, ct);
    }

    private async Task<EnrichTarget[]> LoadTargetsAsync(
        IExternalIdLookupProvider provider, IReadOnlyList<string>? itemIds, int limit, CancellationToken ct)
    {
        var loaded = itemIds is { Count: > 0 }
            ? await items.ListByIdsAsync(itemIds.Select(ObjectId.Parse).ToArray(), ct)
            : await items.ListEnrichmentCandidatesAsync(
                provider.CompletionMarkerKey, Math.Clamp(limit, 1, 200), ct);

        return loaded.Select(item => new EnrichTarget(item, ExternalIdFor(item, provider))).ToArray();
    }

    /// <summary>
    /// 已有本 provider 的識別碼就直接用，不必再繞外部來源反查一次；
    /// 否則退回外部來源的識別碼。兩者皆無代表這是手動建檔且未綁定的品項——不猜。
    /// </summary>
    private static string? ExternalIdFor(Item item, IExternalIdLookupProvider provider)
    {
        if (item.Attributes.TryGetValue(provider.ExternalIdAttributeKey, out var externalId)
            && !externalId.IsBsonNull)
        {
            return $"{provider.Key}:{externalId.ToInt64()}";
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
    ///
    /// 過濾完再套 <see cref="ExternalItem.FillOnlyIfAbsent"/>：provider 宣告為軟寫入的欄位
    /// 在品項已有值時讓位，其餘一律覆蓋。name / description 走同一條規則，
    /// 不再各自寫一段 if。
    /// </summary>
    private static ItemEnrichment ToEnrichment(Item item, ExternalItem source, HashSet<string> allowedKeys)
    {
        var attributes = source.Attributes
            .Where(pair => allowedKeys.Contains(pair.Key)
                           && ShouldWrite(source, pair.Key, HasAttributeValue(item, pair.Key)))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        var name = !string.IsNullOrWhiteSpace(source.Name)
                   && ShouldWrite(source, ItemFieldKeys.Name, !string.IsNullOrWhiteSpace(item.Name))
            ? source.Name
            : null;

        var description = !string.IsNullOrWhiteSpace(source.Description)
                          && ShouldWrite(
                              source, ItemFieldKeys.Description, !string.IsNullOrWhiteSpace(item.Description))
            ? source.Description
            : null;

        return new ItemEnrichment(item.Id, name, description, attributes);
    }

    private static bool ShouldWrite(ExternalItem source, string fieldKey, bool itemHasValue) =>
        !itemHasValue || !source.FillOnlyIfAbsent.Contains(fieldKey);

    /// <summary>BSON null 與空字串都當作沒有值，否則軟寫入會被空殼擋住。</summary>
    private static bool HasAttributeValue(Item item, string key) =>
        item.Attributes.TryGetValue(key, out var value)
        && !value.IsBsonNull
        && (!value.IsString || !string.IsNullOrWhiteSpace(value.AsString));

    private sealed record EnrichTarget(Item Item, string? ExternalId);
}
