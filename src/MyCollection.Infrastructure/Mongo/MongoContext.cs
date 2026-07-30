using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MyCollection.Domain.Entities;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoContext
{
    public MongoContext(IOptions<MongoOptions> options)
    {
        // 冪等；保證在這個 context 產生任何序列化之前慣例已就緒
        MongoConventions.Register();

        var settings = MongoClientSettings.FromConnectionString(options.Value.ConnectionString);
        settings.ServerApi = new ServerApi(ServerApiVersion.V1);

        var client = new MongoClient(settings);
        Database = client.GetDatabase(options.Value.Database);
    }

    public IMongoDatabase Database { get; }

    public IMongoCollection<User> Users => Database.GetCollection<User>("users");

    public IMongoCollection<Category> Categories => Database.GetCollection<Category>("categories");

    public IMongoCollection<Item> Items => Database.GetCollection<Item>("items");

    public IMongoCollection<ShareLink> ShareLinks => Database.GetCollection<ShareLink>("shareLinks");

    public IMongoCollection<ExternalAccount> ExternalAccounts =>
        Database.GetCollection<ExternalAccount>("externalAccounts");

    public IMongoCollection<SyncJob> SyncJobs => Database.GetCollection<SyncJob>("syncJobs");
}
