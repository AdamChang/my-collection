using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace MyCollection.Infrastructure.Mongo;

/// <summary>
/// 全域 BSON 慣例。必須在任何序列化發生前呼叫一次（Register 具冪等性）。
/// </summary>
public static class MongoConventions
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        ConventionRegistry.Register(
            "mycollection",
            new ConventionPack
            {
                new CamelCaseElementNameConvention(),
                new IgnoreExtraElementsConvention(true),
                new EnumRepresentationConvention(BsonType.String)
            },
            _ => true);

        BsonSerializer.RegisterSerializer(new DateTimeSerializer(DateTimeKind.Utc));
        BsonSerializer.RegisterSerializer(new NullableSerializer<DateTime>(new DateTimeSerializer(DateTimeKind.Utc)));
    }
}
