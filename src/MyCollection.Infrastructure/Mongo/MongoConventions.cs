using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace MyCollection.Infrastructure.Mongo;

/// <summary>
/// 全域 BSON 慣例。必須在任何序列化發生前呼叫一次。
///
/// 註冊在 lock 內完成、旗標最後才設：BsonClassMap 一旦建立就永久快取，
/// 若讓第二個執行緒在註冊完成前提早返回並開始序列化，整個行程都會固定用錯誤的 schema。
/// </summary>
public static class MongoConventions
{
    private static readonly Lock Gate = new();
    private static bool _registered;

    public static void Register()
    {
        lock (Gate)
        {
            if (_registered)
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

            BsonSerializer.TryRegisterSerializer(new UtcOnlyDateTimeSerializer());
            BsonSerializer.TryRegisterSerializer(
                new NullableSerializer<DateTime>(new UtcOnlyDateTimeSerializer()));

            _registered = true;
        }
    }
}

/// <summary>
/// 只接受 Kind = Utc 的 DateTime。
///
/// 預設的 DateTimeSerializer(DateTimeKind.Utc) 只保證讀出來是 UTC，寫入時會呼叫
/// ToUniversalTime() —— .NET 把 Unspecified 當成本地時間，於是 UTC+8 的機器會把
/// 03:00 靜默存成前一天 19:00。購入日期差一天、refresh token 提早失效，都不會拋錯，
/// 只會在某天變成「時間怪怪的」。寧可在寫入當下就爆。
/// </summary>
public sealed class UtcOnlyDateTimeSerializer : SerializerBase<DateTime>
{
    // MongoDB.Bson.Serialization.Serializers.DateTimeSerializer 是 sealed，無法繼承，
    // 改用組合：實際的序列化/反序列化邏輯委派給它，只在寫入前插入 Kind 檢查。
    private static readonly DateTimeSerializer Inner = new(DateTimeKind.Utc);

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException(
                $"DateTime must have Kind=Utc before it is persisted, but was {value.Kind}. " +
                "Normalise the value at the API boundary (treat naive input as UTC).");
        }

        Inner.Serialize(context, args, value);
    }

    public override DateTime Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        => Inner.Deserialize(context, args);
}
