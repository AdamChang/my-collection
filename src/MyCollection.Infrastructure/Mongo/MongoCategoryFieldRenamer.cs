using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Mongo;

/// <summary>
/// 全案第一個跨 collection 的寫入。刻意不做成通用 UnitOfWork：
/// 為一個操作改動每個 repository 的 session 傳遞太貴（ADR-0012 §六 d）。
/// </summary>
public sealed class MongoCategoryFieldRenamer(MongoContext context, IUserContext userContext) : ICategoryFieldRenamer
{
    public async Task<long> RenameAsync(ObjectId categoryId, string oldKey, string newKey, DateTime updatedAt, CancellationToken ct)
    {
        var ownerId = userContext.UserId;
        var oldPath = $"attributes.{oldKey}";
        var newPath = $"attributes.{newKey}";

        var itemFilter = Builders<Item>.Filter;
        var inCategory = itemFilter.And(
            itemFilter.Eq(x => x.OwnerId, ownerId),
            itemFilter.Eq(x => x.CategoryId, categoryId));

        using var session = await context.Database.Client.StartSessionAsync(cancellationToken: ct);

        return await session.WithTransactionAsync(async (s, token) =>
        {
            // 衝突計數必須在 $rename 之前：$rename 會覆蓋目標欄位
            var conflicts = await context.Items.CountDocumentsAsync(s,
                itemFilter.And(inCategory, itemFilter.Exists(oldPath), itemFilter.Exists(newPath)),
                cancellationToken: token);

            if (conflicts > 0)
            {
                throw new ConflictException(
                    $"{conflicts} item(s) already carry '{newKey}' alongside '{oldKey}'; resolve them before renaming.");
            }

            var moved = await context.Items.UpdateManyAsync(s,
                itemFilter.And(inCategory, itemFilter.Exists(oldPath)),
                Builders<Item>.Update.Rename(oldPath, newPath),
                cancellationToken: token);

            // arrayFilters 只改命中的那個元素，不整包置換 fields
            var categoryResult = await context.Categories.UpdateOneAsync(s,
                Builders<Category>.Filter.And(
                    Builders<Category>.Filter.Eq(x => x.Id, categoryId),
                    Builders<Category>.Filter.Eq(x => x.OwnerId, ownerId)),
                Builders<Category>.Update
                    .Set("fields.$[f].key", newKey)
                    .Set(x => x.UpdatedAt, updatedAt),
                new UpdateOptions
                {
                    ArrayFilters = [new BsonDocumentArrayFilterDefinition<BsonDocument>(new BsonDocument("f.key", oldKey))]
                },
                token);

            if (categoryResult.MatchedCount == 0)
            {
                throw new NotFoundException(nameof(Category), categoryId);
            }

            return moved.ModifiedCount;
        }, cancellationToken: ct);
    }
}
