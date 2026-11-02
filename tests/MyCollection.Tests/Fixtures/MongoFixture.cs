using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;
using Testcontainers.MongoDb;

namespace MyCollection.Tests.Fixtures;

public sealed class MongoFixture : IAsyncLifetime
{
    // 釘住 patch 版本：浮動的 mongo:8.0 在 2026-10 指到 8.0.34 後，GitHub Actions runner 上
    // Testcontainers 找不到 mongosh，replica set 起不來，整批 Integration 測試失敗（本機無法重現）。
    // 8.0.30 是最後一次成功部署（2026-09-27）時 mongo:8.0 指向的版本。升級前先在 CI 驗證。
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0.30")
        .WithReplicaSet("rs0")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public string DatabaseName => "mycollection_test";

    public MongoContext Context { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            // 啟動失敗時 xUnit 仍會呼叫 DisposeAsync 刪除容器，事後無法再看 docker logs；
            // 只能在這裡把 mongod 留下的輸出附在例外訊息上，CI 的測試結果才看得到原因。
            var (stdout, stderr) = await _container.GetLogsAsync(timestampsEnabled: false);
            var exitCode = await _container.GetExitCodeAsync();
            throw new InvalidOperationException(
                $"Mongo 測試容器啟動失敗（exit code {exitCode}）。\n--- stdout ---\n{Tail(stdout)}\n--- stderr ---\n{Tail(stderr)}",
                ex);
        }

        Context = new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = ConnectionString,
            Database = DatabaseName
        }));

        await MongoIndexInitializer.EnsureIndexesAsync(Context, CancellationToken.None);
    }

    /// <summary>每個測試開頭呼叫，清空所有 collection 但保留索引。後續計畫會隨新增 collection 擴充。</summary>
    public async Task ResetAsync()
    {
        await Context.Users.DeleteManyAsync(FilterDefinition<User>.Empty);
        await Context.Categories.DeleteManyAsync(FilterDefinition<Category>.Empty);
        await Context.Items.DeleteManyAsync(FilterDefinition<Item>.Empty);
        await Context.ShareLinks.DeleteManyAsync(FilterDefinition<ShareLink>.Empty);
        await Context.ExternalAccounts.DeleteManyAsync(FilterDefinition<ExternalAccount>.Empty);
        await Context.SyncJobs.DeleteManyAsync(FilterDefinition<SyncJob>.Empty);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    // mongod 的結構化 log 很長，結束原因在最後幾行；同一則訊息會出現在每個失敗的測試上，所以只留尾端。
    private static string Tail(string log) => log.Length <= 4000 ? log : log[^4000..];
}

[CollectionDefinition(MongoCollection.Name)]
public sealed class MongoCollection : ICollectionFixture<MongoFixture>
{
    public const string Name = "mongo";
}
