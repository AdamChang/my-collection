namespace MyCollection.Application.Transfer;

/// <summary>
/// 圖片封存檔的標頭。刻意不列檔案清單——zip 的中央目錄已經是清單了，
/// 再寫一份只會多出一個必須跟著對齊的事實來源。
///
/// 帶 <see cref="OwnerId"/> 是為了讓匯入端有單一權威來源可比對，
/// 不必從每一條 entry 路徑各自反推「這包到底是誰的」。
/// </summary>
public sealed class ImageArchiveManifest
{
    /// <summary>
    /// 從 2 起跳。1 是舊的「整份收藏資料」封存格式（含品類、品項、分享連結），
    /// 與這裡的結構完全不同；沿用同一個號碼會讓舊檔通過版本檢查，
    /// 然後在下游因為缺 ownerId 炸出一個沒人看得懂的錯誤。
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>舊的資料封存檔版本，只用於產生一句看得懂的拒絕訊息。</summary>
    public const int LegacyDataArchiveSchemaVersion = 1;

    /// <summary>zip 內 manifest 的固定檔名。</summary>
    public const string FileName = "manifest.json";

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public DateTime ExportedAt { get; set; }

    /// <summary>
    /// 匯出者的 id。共用同一個 MongoDB 時，同一個帳號在每台機器上都是同一份文件，
    /// 因此這個值必須與匯入端的登入者相同——不同就代表拿錯檔案。
    /// </summary>
    public required string OwnerId { get; set; }

    /// <summary>zip 內實際帶走的圖檔數（一張圖有 full／card／thumb 三個檔）。</summary>
    public int FileCount { get; set; }

    /// <summary>DB 有記錄、但匯出當下磁碟上找不到的檔案。</summary>
    public List<MissingImageFile> Missing { get; set; } = [];
}

public sealed class MissingImageFile
{
    public required string ItemName { get; set; }

    public required string Path { get; set; }
}

/// <summary>封存檔無法解析、版本不支援，或不屬於當前帳號。由全域處理器轉成 400。</summary>
public sealed class InvalidArchiveException(string message, Exception? innerException = null)
    : Exception(message, innerException);
