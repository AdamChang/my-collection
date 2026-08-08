namespace MyCollection.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Local | Gcs。</summary>
    public string Provider { get; init; } = "Local";

    public string LocalRoot { get; init; } = "data/media";

    public string Bucket { get; init; } = string.Empty;
}
