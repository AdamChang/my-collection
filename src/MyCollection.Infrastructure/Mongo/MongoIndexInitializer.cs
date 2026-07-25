using MongoDB.Driver;
using MyCollection.Domain.Entities;

namespace MyCollection.Infrastructure.Mongo;

/// <summary>
/// 啟動時建立所有索引。CreateOne/CreateMany 具冪等性：同名同定義的索引會被忽略。
/// 後續計畫會在這裡持續追加 categories / items / shareLinks / externalAccounts / syncJobs 的索引。
/// </summary>
public static class MongoIndexInitializer
{
    public static async Task EnsureIndexesAsync(MongoContext context, CancellationToken ct)
    {
        await context.Users.Indexes.CreateOneAsync(
            new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(x => x.Email),
                new CreateIndexOptions { Name = "ux_users_email", Unique = true }),
            cancellationToken: ct);

        await context.Users.Indexes.CreateOneAsync(
            new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(x => x.RefreshTokenHash),
                new CreateIndexOptions { Name = "ix_users_refreshTokenHash", Sparse = true }),
            cancellationToken: ct);
    }
}
