using MongoDB.Driver;
using MyCollection.Domain.Entities;

namespace MyCollection.Infrastructure.Mongo;

public static class SystemCategorySeeder
{
    public static async Task SeedAsync(
        MongoContext context,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var writes = SystemCategoryDefinitions.Create(now)
            .Select(category => new UpdateOneModel<Category>(
                Builders<Category>.Filter.Eq(x => x.Id, category.Id),
                Builders<Category>.Update
                    .Set(x => x.OwnerId, (MongoDB.Bson.ObjectId?)null)
                    .Set(x => x.Name, category.Name)
                    .Set(x => x.Icon, category.Icon)
                    .Set(x => x.Kind, category.Kind)
                    .Set(x => x.Fields, category.Fields)
                    .Set(x => x.UpdatedAt, now)
                    .SetOnInsert(x => x.CreatedAt, now))
            {
                IsUpsert = true
            })
            .Cast<WriteModel<Category>>()
            .ToArray();

        await context.Categories.BulkWriteAsync(writes, cancellationToken: ct);
    }
}
