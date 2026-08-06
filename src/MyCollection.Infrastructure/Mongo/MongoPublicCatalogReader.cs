using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MyCollection.Application.Categories;
using MyCollection.Application.Sharing;
using MyCollection.Domain.Entities;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoPublicCatalogReader(MongoContext context) : IPublicCatalogReader
{
    private static readonly FilterDefinitionBuilder<Item> Filter = Builders<Item>.Filter;

    /// <summary>
    /// 白名單投影。內部 Item 之後新增任何欄位都不會自動出現在公開回應上——
    /// 必須有人主動把它加進這裡。StorageLocation 刻意永遠不加進來：它沒有對應的 IncludeXxx 旗標，
    /// 這是唯一的防線（見 ADR-0008）。
    /// </summary>
    private static readonly ProjectionDefinition<Item> BaseProjection = Builders<Item>.Projection
        .Include(x => x.CategoryId)
        .Include(x => x.Name)
        .Include(x => x.Description)
        .Include(x => x.Tags)
        .Include(x => x.Images)
        .Include(x => x.Attributes)
        .Include(x => x.DisplayMode);

    public async Task<IReadOnlyList<PublicItemProjection>> ListItemsAsync(
        ObjectId ownerId,
        ShareScope scope,
        IReadOnlyList<ObjectId> categoryIds,
        bool includePrice,
        bool includeRating,
        CancellationToken ct)
    {
        var filters = new List<FilterDefinition<Item>> { Filter.Eq(x => x.OwnerId, ownerId) };

        // Category scope 且清單為空時，$in: [] 天然不匹配任何文件
        filters.Add(scope == ShareScope.Showcase
            ? Filter.Eq(x => x.IsShowcased, true)
            : Filter.In(x => x.CategoryId, categoryIds));

        var projection = BaseProjection;
        if (includePrice)
        {
            projection = projection.Include("acquisition.price").Include("acquisition.acquiredAt");
        }

        if (includeRating)
        {
            projection = projection.Include(x => x.Rating);
        }

        var documents = await context.Items
            .Find(Filter.And(filters))
            .Project<BsonDocument>(projection)
            // _id 作為決定性次要鍵：updatedAt 只有毫秒精度，極易並列
            .Sort(Builders<Item>.Sort.Descending(x => x.UpdatedAt).Descending(x => x.Id))
            .ToListAsync(ct);

        return documents.Select(ToProjection).ToArray();
    }

    public async Task<IReadOnlyDictionary<ObjectId, PublicCategoryInfo>> ListCategoriesAsync(
        ObjectId ownerId, CancellationToken ct)
    {
        var categories = await context.Categories
            // 明確指定 ObjectId?[]：集合運算式無法從 [ownerId, null] 推斷可空型別
            .Find(Builders<Category>.Filter.In(x => x.OwnerId, new ObjectId?[] { ownerId, null }))
            .Project<BsonDocument>(Builders<Category>.Projection
                .Include(x => x.Name)
                .Include(x => x.DefaultDisplayMode)
                .Include(x => x.Fields))
            .ToListAsync(ct);

        return categories.ToDictionary(
            d => d["_id"].AsObjectId,
            d =>
            {
                var fields = d.GetValue("fields", new BsonArray()).AsBsonArray
                    .Select(f => BsonSerializer.Deserialize<CategoryField>(f.AsBsonDocument))
                    .Where(f => f.ShowOnCard)
                    .Select(CategoryMapper.ToDto)
                    .ToArray();

                return new PublicCategoryInfo(
                    d.GetValue("name", BsonString.Empty).AsString,
                    Enum.Parse<DisplayMode>(d.GetValue("defaultDisplayMode", "List").AsString, ignoreCase: true),
                    fields);
            });
    }

    private static PublicItemProjection ToProjection(BsonDocument document) => new()
    {
        Id = document["_id"].AsObjectId,
        CategoryId = document.GetValue("categoryId", BsonNull.Value) is { IsBsonNull: false } c ? c.AsObjectId : ObjectId.Empty,
        Name = document.GetValue("name", BsonString.Empty).AsString,
        Description = document.GetValue("description", BsonNull.Value) is { IsBsonNull: false } d ? d.AsString : null,
        Tags = document.GetValue("tags", new BsonArray()).AsBsonArray.Select(t => t.AsString).ToList(),
        Images = document.GetValue("images", new BsonArray()).AsBsonArray
            .Select(i => BsonSerializer.Deserialize<ItemImage>(i.AsBsonDocument))
            .ToList(),
        Attributes = document.GetValue("attributes", new BsonDocument()).AsBsonDocument,
        DisplayMode = document.GetValue("displayMode", BsonNull.Value) is { IsBsonNull: false } dm
            ? Enum.Parse<DisplayMode>(dm.AsString, ignoreCase: true)
            : null,
        Price = document.GetValue("acquisition", BsonNull.Value) is { IsBsonNull: false } a
                && a.AsBsonDocument.GetValue("price", BsonNull.Value) is { IsBsonNull: false } p
            ? BsonSerializer.Deserialize<Money>(p.AsBsonDocument)
            : null,
        AcquiredAt = document.GetValue("acquisition", BsonNull.Value) is { IsBsonNull: false } acq
                && acq.AsBsonDocument.GetValue("acquiredAt", BsonNull.Value) is { IsBsonNull: false } acquiredAt
            ? acquiredAt.ToUniversalTime()
            : null,
        Rating = document.GetValue("rating", BsonNull.Value) is { IsBsonNull: false } r ? r.AsInt32 : null
    };
}
