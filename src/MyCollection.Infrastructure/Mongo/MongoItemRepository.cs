using MongoDB.Bson;
using MongoDB.Bson.Serialization;
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

        // 空清單代表「沒有任何品類宣告被要求的欄位」，回零筆是正確結果——不可退回不限縮。
        if (spec.CategoryIds is { } categoryIds)
        {
            filters.Add(Filter.In(x => x.CategoryId, categoryIds));
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

        foreach (var key in spec.MissingAttributes ?? [])
        {
            // MongoDB 的 {field: null} 同時匹配「值為 null」與「欄位不存在」，
            // 所以三態（missing / null / ""）只需要這兩個條件。key 的安全性同上。
            filters.Add(Filter.Or(
                Filter.Eq($"attributes.{key}", BsonNull.Value),
                Filter.Eq($"attributes.{key}", string.Empty)));
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

    public async Task<IReadOnlyList<string>> ListPlatformsAsync(ObjectId? categoryId, CancellationToken ct)
    {
        var filter = Filter.And(
            OwnerFilter,
            categoryId is { } id ? Filter.Eq(x => x.CategoryId, id) : Filter.Exists("attributes.platform"));

        var platforms = await Items.DistinctAsync<string>("attributes.platform", filter, cancellationToken: ct);

        return (await platforms.ToListAsync(ct)).Order(StringComparer.Ordinal).ToArray();
    }

    public Task<long> CountByCategoryAsync(ObjectId categoryId, CancellationToken ct) =>
        Items.CountDocumentsAsync(
            Filter.And(OwnerFilter, Filter.Eq(x => x.CategoryId, categoryId)),
            cancellationToken: ct);

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
    ///
    /// attributes 不在列舉之內，理由同上但更細一層：整份 $set attributes 會刪掉品項上的
    /// 未宣告屬性（ADR-0012 §三，tech debt C1），而且讀後寫回的呼叫端（圖片 handler）
    /// 會把期間別人寫入的屬性還原成舊值。要改屬性一律走帶 <see cref="AttributeChanges"/> 的多載。
    ///
    /// images 也不在列舉之內（tech debt M1）：表單手上的 images 是讀取當下的舊陣列，
    /// 整份 $set 會把期間上傳的圖蓋掉。圖片只由 AddImageAsync／RemoveImageAsync／SetPrimaryImageAsync
    /// 與精選圖片下載的條件式 $push 寫入。
    /// </summary>
    public Task UpdateAsync(Item item, CancellationToken ct) => UpdateCoreAsync(item, null, ct);

    public Task UpdateAsync(Item item, AttributeChanges attributeChanges, CancellationToken ct) =>
        UpdateCoreAsync(item, attributeChanges, ct);

    private async Task UpdateCoreAsync(Item item, AttributeChanges? attributeChanges, CancellationToken ct)
    {
        item.OwnerId = userContext.UserId;

        var updates = new List<UpdateDefinition<Item>> { ItemFieldsUpdate(item) };

        if (attributeChanges is not null)
        {
            // 以 dotted path 逐鍵寫入。key 已由品類 schema 的 FieldKeyPattern 限定為 camelCase 英數字，
            // 不會含有「.」或「$」而改變路徑語意。
            updates.AddRange(attributeChanges.Set.Select(e =>
                Builders<Item>.Update.Set($"attributes.{e.Name}", e.Value)));
            updates.AddRange(attributeChanges.Unset.Select(key =>
                Builders<Item>.Update.Unset($"attributes.{key}")));
        }

        // OwnerId / Source / ExternalRef / CreatedAt 不在此列：
        // 它們由同步流程與建立流程擁有，使用者更新不得改寫。
        var result = await Items.UpdateOneAsync(
            Filter.And(OwnerFilter, Filter.Eq(x => x.Id, item.Id)),
            Builders<Item>.Update.Combine(updates),
            cancellationToken: ct);

        if (result.MatchedCount == 0)
        {
            throw new NotFoundException(nameof(Item), item.Id);
        }
    }

    private static UpdateDefinition<Item> ItemFieldsUpdate(Item item) =>
        Builders<Item>.Update
            .Set(x => x.CategoryId, item.CategoryId)
            .Set(x => x.Name, item.Name)
            .Set(x => x.Description, item.Description)
            .Set(x => x.Tags, item.Tags)
            .Set(x => x.IsShowcased, item.IsShowcased)
            .Set(x => x.Acquisition, item.Acquisition)
            .Set(x => x.LocationId, item.LocationId)
            .Set(x => x.DisplayMode, item.DisplayMode)
            .Set(x => x.Rating, item.Rating)
            .Set(x => x.StorageLocation, item.StorageLocation)
            .Set(x => x.UpdatedAt, item.UpdatedAt);

    // ── 圖片的原子寫入（tech debt M1） ─────────────────────────────────────────────
    //
    // 三者都用 aggregation pipeline update，讓「主圖是誰、順序是多少」由伺服器依寫入當下的陣列計算。
    // 單純的 $push／$pull 做不到：上傳要知道「附加前是不是空陣列」，刪除要知道「刪完還有沒有主圖」，
    // 在應用端先讀再算，並行時就會算出兩張主圖或重複的 order。設主圖也不能用 arrayFilters——
    // 同一個 update 對 images.$[] 與 images.$[t] 各自 $set isPrimary 會被 MongoDB 判為路徑衝突。

    public async Task<ItemImage> AddImageAsync(ObjectId itemId, ItemImage image, DateTime updatedAt, CancellationToken ct)
    {
        var current = CurrentImages();

        // 呼叫端的值一律包 $literal：pipeline 裡以「$」開頭的字串會被當成欄位路徑。
        var appended = new BsonDocument();
        foreach (var element in image.ToBsonDocument())
        {
            appended[element.Name] = new BsonDocument("$literal", element.Value);
        }

        appended[ImageElements.IsPrimary] = new BsonDocument("$eq", new BsonArray { new BsonDocument("$size", current), 0 });
        appended[ImageElements.Order] = new BsonDocument("$size", current);

        var updated = await Items.FindOneAndUpdateAsync(
            Filter.And(OwnerFilter, Filter.Eq(x => x.Id, itemId)),
            ImagesPipeline(
                updatedAt,
                new BsonDocument("$concatArrays", new BsonArray { current, new BsonArray { appended } })),
            new FindOneAndUpdateOptions<Item> { ReturnDocument = ReturnDocument.After },
            ct) ?? throw new NotFoundException(nameof(Item), itemId);

        return updated.Images.Single(i => i.Id == image.Id);
    }

    public async Task<ItemImage> RemoveImageAsync(ObjectId itemId, string imageId, DateTime updatedAt, CancellationToken ct)
    {
        var remaining = new BsonDocument("$filter", new BsonDocument
        {
            { "input", CurrentImages() },
            { "cond", new BsonDocument("$ne", new BsonArray { $"$$this.{ImageElements.Id}", imageId }) }
        });

        // 在同一個 pipeline 裡先過濾、再依剩下的陣列補主圖與重排：
        // 剩下的圖若沒有任何主圖，第一張晉升；order 一律等於陣列位置。
        var normalised = new BsonDocument("$let", new BsonDocument
        {
            { "vars", new BsonDocument("rest", remaining) },
            {
                "in", new BsonDocument("$let", new BsonDocument
                {
                    {
                        "vars", new BsonDocument("hasPrimary", new BsonDocument("$anyElementTrue", new BsonArray
                        {
                            new BsonDocument("$map", new BsonDocument
                            {
                                { "input", "$$rest" },
                                { "in", new BsonDocument("$eq", new BsonArray { $"$$this.{ImageElements.IsPrimary}", true }) }
                            })
                        }))
                    },
                    {
                        "in", new BsonDocument("$map", new BsonDocument
                        {
                            { "input", new BsonDocument("$range", new BsonArray { 0, new BsonDocument("$size", "$$rest") }) },
                            { "as", "i" },
                            {
                                "in", new BsonDocument("$mergeObjects", new BsonArray
                                {
                                    new BsonDocument("$arrayElemAt", new BsonArray { "$$rest", "$$i" }),
                                    new BsonDocument
                                    {
                                        { ImageElements.Order, "$$i" },
                                        {
                                            ImageElements.IsPrimary, new BsonDocument("$or", new BsonArray
                                            {
                                                new BsonDocument("$eq", new BsonArray
                                                {
                                                    new BsonDocument("$getField", new BsonDocument
                                                    {
                                                        { "field", ImageElements.IsPrimary },
                                                        { "input", new BsonDocument("$arrayElemAt", new BsonArray { "$$rest", "$$i" }) }
                                                    }),
                                                    true
                                                }),
                                                new BsonDocument("$and", new BsonArray
                                                {
                                                    new BsonDocument("$not", new BsonArray { "$$hasPrimary" }),
                                                    new BsonDocument("$eq", new BsonArray { "$$i", 0 })
                                                })
                                            })
                                        }
                                    }
                                })
                            }
                        })
                    }
                })
            }
        });

        var before = await Items.FindOneAndUpdateAsync(
            ImageFilter(itemId, imageId),
            ImagesPipeline(updatedAt, normalised),
            new FindOneAndUpdateOptions<Item> { ReturnDocument = ReturnDocument.Before },
            ct) ?? throw await ImageNotFoundAsync(itemId, imageId, ct);

        return before.Images.Single(i => i.Id == imageId);
    }

    public async Task SetPrimaryImageAsync(ObjectId itemId, string imageId, DateTime updatedAt, CancellationToken ct)
    {
        var flagged = new BsonDocument("$map", new BsonDocument
        {
            { "input", CurrentImages() },
            {
                "in", new BsonDocument("$mergeObjects", new BsonArray
                {
                    "$$this",
                    new BsonDocument(
                        ImageElements.IsPrimary,
                        new BsonDocument("$eq", new BsonArray { $"$$this.{ImageElements.Id}", imageId }))
                })
            }
        });

        var result = await Items.UpdateOneAsync(
            ImageFilter(itemId, imageId),
            ImagesPipeline(updatedAt, flagged),
            cancellationToken: ct);

        if (result.MatchedCount == 0)
        {
            throw await ImageNotFoundAsync(itemId, imageId, ct);
        }
    }

    /// <summary>images 缺欄位或為 null 的舊文件視同空陣列，否則 $size／$concatArrays 會失敗或得到 null。</summary>
    private static BsonDocument CurrentImages() =>
        new("$ifNull", new BsonArray { $"${ImageElements.Images}", new BsonArray() });

    private static UpdateDefinition<Item> ImagesPipeline(DateTime updatedAt, BsonValue images) =>
        Builders<Item>.Update.Pipeline(new EmptyPipelineDefinition<Item>().AppendStage<Item, Item, Item>(
            new BsonDocument("$set", new BsonDocument
            {
                { ImageElements.Images, images },
                { ImageElements.UpdatedAt, new BsonDocument("$literal", new BsonDateTime(updatedAt)) }
            })));

    private FilterDefinition<Item> ImageFilter(ObjectId itemId, string imageId) =>
        Filter.And(
            OwnerFilter,
            Filter.Eq(x => x.Id, itemId),
            Filter.ElemMatch(x => x.Images, i => i.Id == imageId));

    /// <summary>
    /// 條件式寫入沒命中時，只有多查一次才能分辨是品項不存在還是圖片不存在。
    /// 只發生在失敗路徑；兩者對前端與 log 的判讀方向不同，值得這一次查詢。
    /// </summary>
    private async Task<NotFoundException> ImageNotFoundAsync(ObjectId itemId, string imageId, CancellationToken ct)
    {
        var itemExists = await Items.CountDocumentsAsync(
            Filter.And(OwnerFilter, Filter.Eq(x => x.Id, itemId)),
            new CountOptions { Limit = 1 },
            ct) > 0;

        return itemExists
            ? new NotFoundException(nameof(ItemImage), imageId)
            : new NotFoundException(nameof(Item), itemId);
    }

    /// <summary>
    /// pipeline 以字串定址，element 名稱從 class map 取，不手寫：
    /// ItemImage.Id 受 driver 的 id 慣例影響，實際存成什麼不能靠猜。
    /// </summary>
    private static class ImageElements
    {
        public static readonly string Images = ElementName<Item>(nameof(Item.Images));
        public static readonly string UpdatedAt = ElementName<Item>(nameof(Item.UpdatedAt));
        public static readonly string Id = ElementName<ItemImage>(nameof(ItemImage.Id));
        public static readonly string IsPrimary = ElementName<ItemImage>(nameof(ItemImage.IsPrimary));
        public static readonly string Order = ElementName<ItemImage>(nameof(ItemImage.Order));

        private static string ElementName<T>(string member) =>
            BsonClassMap.LookupClassMap(typeof(T)).GetMemberMap(member).ElementName;
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
