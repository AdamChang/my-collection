using System.Text;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;

namespace MyCollection.Application.Transfer;

/// <summary>
/// manifest 一律走 MongoDB 的 Canonical Extended JSON，不用 System.Text.Json。
///
/// ArchiveItem.Attributes 是 BsonDocument，內容由使用者自定的 category schema 決定，
/// 可能含 Decimal128、DateTime、Int64。一般 JSON 會把這些壓成 string 或 number，
/// 來回一趟就失真。Canonical 模式輸出 $oid / $date / $numberDecimal，保證無損。
///
/// 兩種序列化器混用只會讓邊界出錯的機率倍增，所以整份 manifest 統一用這一個。
/// </summary>
public static class ArchiveManifestSerializer
{
    private static readonly JsonWriterSettings WriterSettings = new()
    {
        OutputMode = JsonOutputMode.CanonicalExtendedJson,
        Indent = true
    };

    public static void Write(Stream destination, ArchiveManifest manifest)
    {
        var json = manifest.ToBsonDocument().ToJson(WriterSettings);

        using var writer = new StreamWriter(destination, new UTF8Encoding(false), leaveOpen: true);
        writer.Write(json);
    }

    /// <summary>內容不是合法的 Extended JSON 時擲 <see cref="FormatException"/>。</summary>
    public static ArchiveManifest Read(Stream source)
    {
        using var reader = new StreamReader(source, Encoding.UTF8, leaveOpen: true);
        var json = reader.ReadToEnd();

        return BsonSerializer.Deserialize<ArchiveManifest>(BsonDocument.Parse(json));
    }
}
