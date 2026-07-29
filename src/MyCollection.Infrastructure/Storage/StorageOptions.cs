namespace MyCollection.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Local | Gcs（第一版僅實作 Local）。</summary>
    public string Provider { get; init; } = "Local";

    public string LocalRoot { get; init; } = "data/media";

    /// <summary>
    /// 匯入前自動備份的根目錄。必須位於 LocalRoot 之外：
    /// LocalRoot 由匿名的 /media 端點對外提供。
    /// </summary>
    public string BackupRoot { get; init; } = "data/backups";
}
