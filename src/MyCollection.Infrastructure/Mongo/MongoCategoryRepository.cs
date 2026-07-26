using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoCategoryRepository(MongoContext context, IUserContext userContext) : ICategoryRepository
{
    private IMongoCollection<Category> Categories => context.Categories;

    /// <summary>可見範圍：自己的 + 系統內建。所有查詢一律從這裡起頭。</summary>
    private FilterDefinition<Category> VisibleFilter =>
        Builders<Category>.Filter.In(x => x.OwnerId, new ObjectId?[] { userContext.UserId, null });

    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken ct) =>
        await Categories.Find(VisibleFilter).SortBy(x => x.Name).ToListAsync(ct);

    public Task<Category?> GetAsync(ObjectId id, CancellationToken ct) =>
        Categories
            .Find(Builders<Category>.Filter.And(VisibleFilter, Builders<Category>.Filter.Eq(x => x.Id, id)))
            .FirstOrDefaultAsync(ct)!;

    public Task<Category?> FindByNameAsync(string name, CancellationToken ct) =>
        Categories
            .Find(Builders<Category>.Filter.And(
                Builders<Category>.Filter.Eq(x => x.OwnerId, userContext.UserId),
                Builders<Category>.Filter.Eq(x => x.Name, name)))
            .FirstOrDefaultAsync(ct)!;

    public Task InsertAsync(Category category, CancellationToken ct)
    {
        category.OwnerId = userContext.UserId;
        return Categories.InsertOneAsync(category, cancellationToken: ct);
    }

    public async Task UpdateAsync(Category category, CancellationToken ct)
    {
        var existing = await GetAsync(category.Id, ct)
                       ?? throw new NotFoundException(nameof(Category), category.Id);

        if (existing.OwnerId is null)
        {
            throw new ForbiddenException("System categories cannot be modified.");
        }

        category.OwnerId = userContext.UserId;

        // 與 MongoItemRepository 同理：$set 具名欄位，不用 ReplaceOne，
        // 避免 IgnoreExtraElements 造成文件既有欄位被靜默刪除。
        var update = Builders<Category>.Update
            .Set(x => x.Name, category.Name)
            .Set(x => x.Icon, category.Icon)
            .Set(x => x.Kind, category.Kind)
            .Set(x => x.Fields, category.Fields)
            .Set(x => x.UpdatedAt, category.UpdatedAt);

        await Categories.UpdateOneAsync(
            Builders<Category>.Filter.And(
                Builders<Category>.Filter.Eq(x => x.Id, category.Id),
                Builders<Category>.Filter.Eq(x => x.OwnerId, userContext.UserId)),
            update,
            cancellationToken: ct);
    }

    public async Task DeleteAsync(ObjectId id, CancellationToken ct)
    {
        var result = await Categories.DeleteOneAsync(
            Builders<Category>.Filter.And(
                Builders<Category>.Filter.Eq(x => x.Id, id),
                Builders<Category>.Filter.Eq(x => x.OwnerId, userContext.UserId)),
            ct);

        if (result.DeletedCount == 0)
        {
            throw new NotFoundException(nameof(Category), id);
        }
    }
}
