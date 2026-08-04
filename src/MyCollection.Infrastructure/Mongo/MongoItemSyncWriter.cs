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
            { "externalRef.url", ToBson(item.SourceUrl?.ToString()) },
            { "externalRef.lastSyncedAt", syncedAt },
            { "updatedAt", syncedAt }
        };

        if (item.Description is not null)
        {
            set["description"] = item.Description;
        }

        foreach (var (key, value) in item.Attributes)
        {
            set[$"attributes.{key}"] = ToBson(value);
        }

        // 使用者擁有的欄位只在建立時寫入，後續同步一律不碰。
        //
        // name 也在這裡：Steam 只回得到英文品名，繁體中文由商店補完寫入
        // （見 IItemEnrichWriter）。若同步繼續 $set name，補完寫好的繁中名稱
        // 會在下一次同步被默默改回英文。代價是 Steam 端真的改名不再傳播進來。
        var setOnInsert = new BsonDocument
        {
            { "name", item.Name },
            { "categoryId", categoryId },
            { "source", source.ToString() },
            { "isShowcased", false },
            { "tags", new BsonArray() },
            { "images", new BsonArray() },
            { "acquisition", BsonNull.Value },
            { "locationId", BsonNull.Value },
            { "createdAt", syncedAt }
        };

        return new UpdateOneModel<Item>(
            filter,
            new BsonDocumentUpdateDefinition<Item>(new BsonDocument
            {
                { "$set", set },
                { "$setOnInsert", setOnInsert }
            }))
        {
            IsUpsert = true
        };
    }

    /// <summary>
    /// null 一律映成 BSON null。不可寫成 <c>value is null ? BsonNull.Value : value.ToString()</c>——
    /// BsonNull 與 string 之間沒有共同型別，會觸發 CS0173。
    /// 也不可直接呼叫 <c>BsonValue.Create(null)</c>，那會擲 ArgumentNullException。
    /// </summary>
    private static BsonValue ToBson(object? value) =>
        value is null ? BsonNull.Value : BsonValue.Create(value);
}
