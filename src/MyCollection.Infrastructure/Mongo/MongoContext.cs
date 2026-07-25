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

        var client = new MongoClient(options.Value.ConnectionString);
        Database = client.GetDatabase(options.Value.Database);
    }

    public IMongoDatabase Database { get; }

    public IMongoCollection<User> Users => Database.GetCollection<User>("users");
}
