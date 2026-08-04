using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoItemEnrichWriter(MongoContext context) : IItemEnrichWriter
{
    public async Task<int> ApplyAsync(
        ObjectId ownerId,
        IReadOnlyList<ItemEnrichment> enrichments,
        DateTime enrichedAt,
        string providerKey,
        CancellationToken ct)
    {
        var models = enrichments
            .Where(e => e.Attributes.Count > 0 || e.Name is not null || e.Description is not null)
            .Select(e => BuildModel(ownerId, e, enrichedAt))
            .ToArray();

        if (models.Length == 0)
        {
            return 0;
        }

        try
        {
            var result = await context.Items.BulkWriteAsync(
                models, new BulkWriteOptions { IsOrdered = false }, ct);

            // MatchedCount 而非 ModifiedCount：重跑相同內容不會產生欄位變更，
            // ModifiedCount 會是 0，報告就會謊稱什麼都沒處理。
            return (int)result.MatchedCount;
        }
        catch (MongoBulkWriteException<Item> ex)
        {
            // 部分成功如實記錄，不做全有全無
            return (int)ex.Result.MatchedCount;
        }
        catch (MongoException ex)
        {
            throw new ProviderException(providerKey, $"Bulk write failed: {ex.Message}", ex);
        }
    }

    private static UpdateOneModel<Item> BuildModel(
        ObjectId ownerId, ItemEnrichment enrichment, DateTime enrichedAt)
    {
        // 授權寫在倉儲層：ownerId 條件擺在 filter 開頭。
        // 漏寫的後果是「查無資料」而不是「別人的資料被改」。
        var filter = Builders<Item>.Filter.And(
            Builders<Item>.Filter.Eq(x => x.OwnerId, ownerId),
            Builders<Item>.Filter.Eq(x => x.Id, enrichment.ItemId));

        var set = new BsonDocument { { "updatedAt", enrichedAt } };

        if (enrichment.Name is not null)
        {
            set["name"] = enrichment.Name;
        }

        if (enrichment.Description is not null)
        {
            set["description"] = enrichment.Description;
        }

        foreach (var (key, value) in enrichment.Attributes)
        {
            set[$"attributes.{key}"] = ToBson(value);
        }

        return new UpdateOneModel<Item>(
            filter,
            new BsonDocumentUpdateDefinition<Item>(new BsonDocument { { "$set", set } }))
        {
            // 補完只更新既有品項。IsUpsert 會讓查無此品項時憑空生出一筆殘缺文件。
            IsUpsert = false
        };
    }

    /// <summary>
    /// null 一律映成 BSON null。不可寫成三元運算子直接混 BsonNull 與 string（CS0173），
    /// 也不可呼叫 BsonValue.Create(null)（擲 ArgumentNullException）。
    /// </summary>
    private static BsonValue ToBson(object? value) =>
        value is null ? BsonNull.Value : BsonValue.Create(value);
}
