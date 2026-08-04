using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoItemSyncWriter(MongoContext context) : IItemSyncWriter
{
    public async Task<SyncOutcome> UpsertAsync(
        ObjectId ownerId,
        ObjectId categoryId,
        ItemSource source,
        string providerKey,
        IReadOnlyList<ExternalItem> items,
        DateTime syncedAt,
        CancellationToken ct)
    {
        // 同一批次內重複的 externalId 會在同一個 BulkWrite 觸發唯一索引衝突，先去重
        var distinct = items
            .GroupBy(i => i.ExternalId, StringComparer.Ordinal)
            .Select(g => g.Last())
            .ToArray();

        if (distinct.Length == 0)
        {
            return new SyncOutcome(0, 0, 0);
        }

        var models = distinct.Select(item => BuildModel(ownerId, categoryId, source, providerKey, item, syncedAt));

        BulkWriteResult<Item> result;
        try
        {
            result = await context.Items.BulkWriteAsync(
                models, new BulkWriteOptions { IsOrdered = false }, ct);
        }
        catch (MongoBulkWriteException<Item> ex)
        {
            // 部分成功如實記錄，不做全有全無
            var upserts = ex.Result.Upserts.Count;
            return new SyncOutcome(upserts, (int)ex.Result.MatchedCount, ex.WriteErrors.Count);
        }
        catch (MongoException ex)
        {
            throw new ProviderException(providerKey, $"Bulk write failed: {ex.Message}", ex);
        }

        // Updated 必須用 MatchedCount 而非 ModifiedCount：內容完全相同的第二次同步
        // 不會產生任何欄位變更，ModifiedCount 會是 0，同步報告就會謊稱「什麼都沒處理」。
        // MatchedCount 表達的是「這幾筆已存在並被重新整理過」，才是使用者要看的語意。
        // （MatchedCount 不含 upsert 新建的文件，兩者不會重複計算。）
        return new SyncOutcome(result.Upserts.Count, (int)result.MatchedCount, 0);
    }

    private static UpdateOneModel<Item> BuildModel(
        ObjectId ownerId,
        ObjectId categoryId,
        ItemSource source,
        string providerKey,
        ExternalItem item,
        DateTime syncedAt)
    {
        var filter = Builders<Item>.Filter.And(
            Builders<Item>.Filter.Eq(x => x.OwnerId, ownerId),
            Builders<Item>.Filter.Eq("externalRef.provider", providerKey),
            Builders<Item>.Filter.Eq("externalRef.externalId", item.ExternalId));

        var set = new BsonDocument
        {
            { "externalRef.url", Literal(item.SourceUrl?.ToString()) },
            { "externalRef.lastSyncedAt", Literal(syncedAt) },
            { "updatedAt", Literal(syncedAt) },

            // Aggregation pipeline 沒有 $setOnInsert。$ifNull 讓建立時補齊預設值，
            // 同時保留既有品項的使用者欄位；空陣列與 false 都不是 null，會原樣保留。
            { "name", IfNull("name", item.Name) },
            { "categoryId", IfNull("categoryId", categoryId) },
            { "source", IfNull("source", source.ToString()) },
            { "isShowcased", IfNull("isShowcased", false) },
            { "tags", IfNull("tags", new BsonArray()) },
            { "images", IfNull("images", new BsonArray()) },
            { "acquisition", IfNull("acquisition", null) },
            { "locationId", IfNull("locationId", null) },
            { "createdAt", IfNull("createdAt", syncedAt) }
        };

        if (item.Description is not null)
        {
            set["description"] = Literal(item.Description);
        }

        foreach (var (key, value) in item.Attributes)
        {
            var path = $"attributes.{key}";
            set[path] = item.FillOnlyIfAbsent.Contains(key)
                ? FillIfMissingNullOrEmpty(path, value)
                : Literal(value);
        }

        var pipeline = PipelineDefinition<Item, Item>.Create(
            [new BsonDocument("$set", set)]);

        return new UpdateOneModel<Item>(
            filter,
            new PipelineUpdateDefinition<Item>(pipeline))
        {
            IsUpsert = true
        };
    }

    private static BsonDocument IfNull(string path, object? fallback) =>
        new("$ifNull", new BsonArray { $"${path}", Literal(fallback) });

    private static BsonDocument FillIfMissingNullOrEmpty(string path, object? value) =>
        new("$cond", new BsonArray
        {
            new BsonDocument("$or", new BsonArray
            {
                new BsonDocument("$in", new BsonArray
                {
                    new BsonDocument("$type", $"${path}"),
                    new BsonArray { "missing", "null" }
                }),
                new BsonDocument("$eq", new BsonArray { $"${path}", "" })
            }),
            Literal(value),
            $"${path}"
        });

    // Pipeline 會把以 '$' 開頭的字串解讀為欄位路徑；所有 payload 常數都必須包成 literal。
    private static BsonDocument Literal(object? value) => new("$literal", ToBson(value));

    /// <summary>
    /// null 一律映成 BSON null。不可寫成 <c>value is null ? BsonNull.Value : value.ToString()</c>——
    /// BsonNull 與 string 之間沒有共同型別，會觸發 CS0173。
    /// 也不可直接呼叫 <c>BsonValue.Create(null)</c>，那會擲 ArgumentNullException。
    /// </summary>
    private static BsonValue ToBson(object? value) =>
        value is null ? BsonNull.Value : BsonValue.Create(value);
}
