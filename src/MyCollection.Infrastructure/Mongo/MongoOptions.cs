namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    public string ConnectionString { get; init; } = "mongodb://localhost:27017";
    public string Database { get; init; } = "mycollection";
}
