using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoItemRepository(MongoContext context, IUserContext userContext) : IItemRepository
{
    private static readonly FilterDefinitionBuilder<Item> Filter = Builders<Item>.Filter;

    private IMongoCollection<Item> Items => context.Items;

    /// <summary>
    /// 所有查詢的起點。忘記加條件的後果是查不到資料，而不是洩漏資料。
    /// </summary>
    private FilterDefinition<Item> OwnerFilter => Filter.Eq(x => x.OwnerId, userContext.UserId);

    public Task<Item?> GetAsync(ObjectId id, CancellationToken ct) =>
        Items.Find(Filter.And(OwnerFilter, Filter.Eq(x => x.Id, id))).FirstOrDefaultAsync(ct)!;

    public async Task<PagedResult<Item>> SearchAsync(ItemQuerySpec spec, CancellationToken ct)
    {
        var filters = new List<FilterDefinition<Item>> { OwnerFilter };

        if (spec.CategoryId is { } categoryId)
        {
            filters.Add(Filter.Eq(x => x.CategoryId, categoryId));
        }

        if (spec.IsShowcased is { } showcased)
        {
            filters.Add(Filter.Eq(x => x.IsShowcased, showcased));
        }

        if (spec.Tags is { Count: > 0 })
        {
            filters.Add(Filter.All(x => x.Tags, spec.Tags));
        }

        foreach (var (key, value) in spec.Attributes ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            // key 未經 schema 驗證——它直接來自查詢字串的 attr.{key}，呼叫端可以送任意字串。
            // 不構成注入的理由是這兩點，不是「key 已被約束」：
            //   1. key 永遠被 "attributes." 前綴包住，無法成為頂層的 $ 運算子
            //   2. value 的型別是 string，無法變成 {$ne: null} 之類的文件
            // 後果僅止於「可以查未宣告的屬性鍵」，查無資料而已，且擁有者條件仍然生效。
            filters.Add(Filter.Eq($"attributes.{key}", value));
        }

        if (!string.IsNullOrWhiteSpace(spec.Search))
        {
            filters.Add(Filter.Text(spec.Search));
        }

        var filter = Filter.And(filters);
        var page = Math.Max(spec.Page, 1);
        var pageSize = Math.Clamp(spec.PageSize, 1, 200);

        var total = await Items.CountDocumentsAsync(filter, cancellationToken: ct);

        // Id 是決定性的次要排序鍵。BSON DateTime 只有毫秒精度，同一批寫入的 updatedAt
        // 極易完全相同；排序鍵有並列時 MongoDB 不保證跨查詢的順序穩定，使用者翻頁就會
        // 看到重複或漏掉的品項。分頁需要全序排序鍵。
        var items = await Items
            .Find(filter)
            .Sort(Builders<Item>.Sort.Descending(x => x.UpdatedAt).Descending(x => x.Id))
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Item>(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<string>> ListTagsAsync(CancellationToken ct)
    {
        var tags = await Items.DistinctAsync<string>("tags", OwnerFilter, cancellationToken: ct);

        return (await tags.ToListAsync(ct)).Order(StringComparer.Ordinal).ToArray();
    }

    public Task InsertAsync(Item item, CancellationToken ct)
    {
        item.OwnerId = userContext.UserId;
        return Items.InsertOneAsync(item, cancellationToken: ct);
    }

    /// <summary>
    /// 刻意用 $set 具名欄位而非 ReplaceOne。
    ///
    /// MongoConventions 註冊了 IgnoreExtraElementsConvention(true)——這是滾動式 schema 演進
    /// 的必要條件，但代價是反序列化會丟掉實體沒宣告的欄位。若用 ReplaceOne 把實體整個寫回去，
    /// 任何一次「欄位改名 → 舊欄位變成 extra element → 使用者編輯該筆」就會永久刪掉舊資料。
    /// $set 只碰列舉出來的欄位，文件裡的其他東西原封不動。
    /// </summary>
    public async Task UpdateAsync(Item item, CancellationToken ct)
    {
        item.OwnerId = userContext.UserId;

        var update = Builders<Item>.Update
            .Set(x => x.CategoryId, item.CategoryId)
            .Set(x => x.Name, item.Name)
            .Set(x => x.Description, item.Description)
            .Set(x => x.Images, item.Images)
            .Set(x => x.Tags, item.Tags)
            .Set(x => x.IsShowcased, item.IsShowcased)
            .Set(x => x.Acquisition, item.Acquisition)
            .Set(x => x.LocationId, item.LocationId)
            .Set(x => x.Attributes, item.Attributes)
            .Set(x => x.UpdatedAt, item.UpdatedAt);

        // OwnerId / Source / ExternalRef / CreatedAt 不在此列：
        // 它們由同步流程與建立流程擁有，使用者更新不得改寫。
        var result = await Items.UpdateOneAsync(
            Filter.And(OwnerFilter, Filter.Eq(x => x.Id, item.Id)),
            update,
            cancellationToken: ct);

        if (result.MatchedCount == 0)
        {
            throw new NotFoundException(nameof(Item), item.Id);
        }
    }

    public async Task DeleteAsync(ObjectId id, CancellationToken ct)
    {
        var result = await Items.DeleteOneAsync(Filter.And(OwnerFilter, Filter.Eq(x => x.Id, id)), ct);

        if (result.DeletedCount == 0)
        {
            throw new NotFoundException(nameof(Item), id);
        }
    }

    public async Task<IReadOnlyList<Item>> ListEnrichmentCandidatesAsync(
        string markerKey, int limit, CancellationToken ct)
    {
        var filter = Filter.And(
            OwnerFilter,
            Filter.Ne(x => x.ExternalRef, null),
            Filter.Exists($"attributes.{markerKey}", false));

        return await Items
            .Find(filter)
            .SortBy(x => x.Id)
            .Limit(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Item>> ListByIdsAsync(IReadOnlyList<ObjectId> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await Items
            .Find(Filter.And(OwnerFilter, Filter.In(x => x.Id, ids)))
            .ToListAsync(ct);
    }
}
