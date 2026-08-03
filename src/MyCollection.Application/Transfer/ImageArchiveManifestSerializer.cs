using System.Text.Json;

namespace MyCollection.Application.Transfer;

/// <summary>
/// manifest 只有原生型別欄位（int／DateTime／string），用 System.Text.Json 即可。
/// 舊版走 MongoDB 的 Canonical Extended JSON 是因為當時 manifest 內含 BsonDocument
/// 與 ObjectId；那些欄位已經不存在，連帶那套序列化器的理由也不存在了。
/// </summary>
public static class ImageArchiveManifestSerializer
{
    /// <summary>
    /// manifest 的大小上限。這是純粹的記憶體配置護欄——<see cref="JsonDocument"/> 會把
    /// 整份內容讀進來。System.Text.Json 預設有 64 層深度上限，不需要舊版那種
    /// 為了避開 StackOverflow 而設的 64 MB 閘門。
    /// </summary>
    public const long MaxBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task WriteAsync(Stream destination, ImageArchiveManifest manifest, CancellationToken ct) =>
        await JsonSerializer.SerializeAsync(destination, manifest, Options, ct);

    /// <summary>
    /// 解析 manifest。任何解析失敗、版本不符、必填欄位缺漏都轉成
    /// <see cref="InvalidArchiveException"/>，呼叫端只需要處理這一種例外。
    /// </summary>
    /// <exception cref="InvalidArchiveException">內容無法解析，或 schemaVersion 不受支援。</exception>
    public static ImageArchiveManifest Read(Stream source)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(source);
        }
        catch (JsonException exception)
        {
            throw new InvalidArchiveException("封存檔的 manifest 不是合法的 JSON。", exception);
        }

        using (document)
        {
            EnsureSupportedVersion(document.RootElement);

            try
            {
                // required 欄位缺漏時 System.Text.Json 會擲 JsonException，
                // 所以「manifest 沒有 ownerId」也走這條路徑，不會回傳半個物件。
                return document.Deserialize<ImageArchiveManifest>(Options)!;
            }
            catch (JsonException exception)
            {
                throw new InvalidArchiveException("封存檔的 manifest 內容無法反序列化。", exception);
            }
        }
    }

    private static void EnsureSupportedVersion(JsonElement root)
    {
        var version = root.ValueKind == JsonValueKind.Object
                      && root.TryGetProperty("schemaVersion", out var value)
                      && value.TryGetInt32(out var parsed)
            ? parsed
            : (int?)null;

        if (version == ImageArchiveManifest.CurrentSchemaVersion)
        {
            return;
        }

        throw new InvalidArchiveException(
            version == ImageArchiveManifest.LegacyDataArchiveSchemaVersion
                ? "這是舊版的「收藏資料」封存檔，新版匯入只接受圖片封存檔。"
                : $"封存檔的 schemaVersion 不受支援（預期 {ImageArchiveManifest.CurrentSchemaVersion}，"
                  + $"實際為 {version?.ToString() ?? "缺漏"}）。");
    }
}
