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
///
/// 注意：這裡只有壓縮檔內的圖片是以串流方式讀寫（一次一張），manifest 本身不是。
/// <see cref="Write"/> 是先把整份 <see cref="ArchiveManifest"/> 轉成 BsonDocument 再轉成
/// 一整條字串才寫出，記憶體用量與收藏的中繼資料量成正比；但 <see cref="ArchiveManifest.Items"/>
/// 本來就是 List&lt;T&gt;，呼叫 Write 之前整份收藏已經在記憶體裡了，所以在 JSON 層做串流
/// 並不會改變真正的記憶體上限，不值得為此犧牲程式碼的簡單。個人收藏等級的資料量下這只有幾 MB。
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

    /// <summary>
    /// 解析封存檔的 manifest。schemaVersion 不吻合、或內容不是合法的 Extended JSON 時，
    /// 一律擲 <see cref="InvalidArchiveException"/>，呼叫端只需要處理這一種例外。
    ///
    /// 內部包了 MongoDB.Bson 實際會丟出的整組例外：對空白／亂碼輸入是 FormatException，
    /// 對被截斷的 JSON 卻是 InvalidOperationException（"ReadEndDocument can only be
    /// called when State is EndOfDocument"）——文件只講前者，但實務上檔案損毀多半是後者。
    ///
    /// 深度限制：MongoDB 的 JsonReader 對巢狀深度沒有上限，餵進極深巢狀的 JSON 會直接
    /// 觸發 StackOverflowException，這在 .NET 是無法攔截、會直接終止行程的例外。這裡沒有
    /// 做防禦；呼叫端（匯入端點）必須在呼叫 Read 之前就限制輸入檔案大小。
    /// </summary>
    /// <exception cref="InvalidArchiveException">
    /// schemaVersion 與 <see cref="ArchiveManifest.CurrentSchemaVersion"/> 不符，或內容無法解析。
    /// </exception>
    public static ArchiveManifest Read(Stream source)
    {
        using var reader = new StreamReader(source, Encoding.UTF8, leaveOpen: true);
        var json = reader.ReadToEnd();

        BsonDocument document;
        try
        {
            document = BsonDocument.Parse(json);
        }
        catch (Exception exception) when (IsBsonParseFailure(exception))
        {
            throw new InvalidArchiveException("封存檔的 manifest 不是合法的 Extended JSON。", exception);
        }

        if (!document.TryGetValue("schemaVersion", out var schemaVersionValue)
            || !schemaVersionValue.IsNumeric
            || schemaVersionValue.ToInt32() != ArchiveManifest.CurrentSchemaVersion)
        {
            var actual = schemaVersionValue is null or BsonNull ? "缺漏" : schemaVersionValue.ToString();
            throw new InvalidArchiveException(
                $"封存檔的 schemaVersion 不受支援（預期 {ArchiveManifest.CurrentSchemaVersion}，實際為 {actual}）。");
        }

        try
        {
            return BsonSerializer.Deserialize<ArchiveManifest>(document);
        }
        catch (Exception exception) when (IsBsonParseFailure(exception))
        {
            throw new InvalidArchiveException("封存檔的 manifest 內容無法反序列化。", exception);
        }
    }

    private static bool IsBsonParseFailure(Exception exception) =>
        exception is FormatException or InvalidOperationException or BsonException or ArgumentException;
}
