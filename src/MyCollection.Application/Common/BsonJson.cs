using System.Text.Json;
using MongoDB.Bson;

namespace MyCollection.Application.Common;

/// <summary>
/// API 邊界的 JSON ⇄ BSON 轉換。System.Text.Json 的 JsonElement 不能直接餵給 driver，
/// 而 BsonDocument 序列化成 JSON 時會產生 Extended JSON（$date、$numberLong），
/// 因此兩個方向都要自己走一遍。
/// </summary>
public static class BsonJson
{
    public static BsonDocument ToBson(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Attributes must be a JSON object.", nameof(element));
        }

        var document = new BsonDocument();
        foreach (var property in element.EnumerateObject())
        {
            document[property.Name] = ToBsonValue(property.Value);
        }

        return document;
    }

    public static Dictionary<string, object?> ToDictionary(BsonDocument document) =>
        document.Elements.ToDictionary(e => e.Name, e => ToClrValue(e.Value));

    private static BsonValue ToBsonValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => ToBson(element),
        JsonValueKind.Array => new BsonArray(element.EnumerateArray().Select(ToBsonValue)),
        JsonValueKind.String => new BsonString(element.GetString()!),
        JsonValueKind.Number => element.TryGetInt32(out var i)
            ? new BsonInt32(i)
            : element.TryGetInt64(out var l)
                ? new BsonInt64(l)
                : new BsonDouble(element.GetDouble()),
        JsonValueKind.True => BsonBoolean.True,
        JsonValueKind.False => BsonBoolean.False,
        JsonValueKind.Null or JsonValueKind.Undefined => BsonNull.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(element), element.ValueKind, "Unsupported JSON value.")
    };

    private static object? ToClrValue(BsonValue value) => value.BsonType switch
    {
        BsonType.Document => ToDictionary(value.AsBsonDocument),
        BsonType.Array => value.AsBsonArray.Select(ToClrValue).ToArray(),
        BsonType.String => value.AsString,
        BsonType.Int32 => value.AsInt32,
        BsonType.Int64 => value.AsInt64,
        BsonType.Double => value.AsDouble,
        BsonType.Decimal128 => (decimal)value.AsDecimal128,
        BsonType.Boolean => value.AsBoolean,
        BsonType.DateTime => value.ToUniversalTime(),
        BsonType.Null => null,
        _ => value.ToString()
    };
}
