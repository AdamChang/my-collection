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

        await context.Categories.Indexes.CreateOneAsync(
            new CreateIndexModel<Category>(
                Builders<Category>.IndexKeys.Ascending(x => x.OwnerId).Ascending(x => x.Name),
                new CreateIndexOptions { Name = "ix_categories_owner" }),
            cancellationToken: ct);

        await context.Items.Indexes.CreateManyAsync(
            [
                // 首頁牆面
                new CreateIndexModel<Item>(
                    Builders<Item>.IndexKeys
                        .Ascending(x => x.OwnerId)
                        .Ascending(x => x.IsShowcased)
                        .Descending(x => x.UpdatedAt),
                    new CreateIndexOptions { Name = "ix_items_showcase" }),

                // 品類瀏覽
                new CreateIndexModel<Item>(
                    Builders<Item>.IndexKeys
                        .Ascending(x => x.OwnerId)
                        .Ascending(x => x.CategoryId)
                        .Descending(x => x.UpdatedAt),
                    new CreateIndexOptions { Name = "ix_items_category" }),

                // 標籤篩選
                new CreateIndexModel<Item>(
                    Builders<Item>.IndexKeys
                        .Ascending(x => x.OwnerId)
                        .Ascending(x => x.Tags),
                    new CreateIndexOptions { Name = "ix_items_tags" }),

                // 同步冪等性的地基：upsert 依賴此唯一索引避免重複品項。
                // sparse 讓沒有 externalRef 的手動品項不參與唯一性檢查。
                new CreateIndexModel<Item>(
                    Builders<Item>.IndexKeys
                        .Ascending(x => x.OwnerId)
                        .Ascending("externalRef.provider")
                        .Ascending("externalRef.externalId"),
                    new CreateIndexOptions { Name = "ux_items_externalRef", Unique = true, Sparse = true }),

                // 全文搜尋
                new CreateIndexModel<Item>(
                    Builders<Item>.IndexKeys.Text(x => x.Name).Text(x => x.Description),
                    new CreateIndexOptions { Name = "tx_items_text" })
            ],
            cancellationToken: ct);
    }
}
