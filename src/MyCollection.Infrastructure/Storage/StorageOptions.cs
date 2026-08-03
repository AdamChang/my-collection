namespace MyCollection.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Local | Gcs（第一版僅實作 Local）。</summary>
    public string Provider { get; init; } = "Local";

    public string LocalRoot { get; init; } = "data/media";
}
