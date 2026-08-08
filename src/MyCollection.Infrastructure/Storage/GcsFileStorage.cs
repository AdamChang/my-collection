using Google;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Options;
using MyCollection.Application.Common;

namespace MyCollection.Infrastructure.Storage;

public sealed class GcsFileStorage(StorageClient client, IOptions<StorageOptions> options) : IFileStorage
{
    private readonly string _bucket = string.IsNullOrWhiteSpace(options.Value.Bucket)
        ? throw new InvalidOperationException("Storage:Bucket is required for GCS storage.")
        : options.Value.Bucket;

    public async Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct)
    {
        relativePath = StoragePath.Validate(relativePath);
        var contentType = relativePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
            ? "image/webp"
            : "application/octet-stream";

        await client.UploadObjectAsync(_bucket, relativePath, contentType, content, cancellationToken: ct);
        return relativePath;
    }

    public async Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct)
    {
        relativePath = StoragePath.Validate(relativePath);
        var content = new MemoryStream();

        try
        {
            await client.DownloadObjectAsync(_bucket, relativePath, content, cancellationToken: ct);
            content.Position = 0;
            return content;
        }
        catch (GoogleApiException exception) when (exception.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await content.DisposeAsync();
            return null;
        }
        catch
        {
            await content.DisposeAsync();
            throw;
        }
    }

    public async Task DeleteAsync(string relativePath, CancellationToken ct)
    {
        relativePath = StoragePath.Validate(relativePath);

        try
        {
            await client.DeleteObjectAsync(_bucket, relativePath, cancellationToken: ct);
        }
        catch (GoogleApiException exception) when (exception.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // IFileStorage delete is intentionally idempotent.
        }
    }

    public async Task DeleteDirectoryAsync(string relativePrefix, CancellationToken ct)
    {
        relativePrefix = StoragePath.Validate(relativePrefix).TrimEnd('/') + "/";

        await foreach (var item in client.ListObjectsAsync(_bucket, relativePrefix).WithCancellation(ct))
        {
            await client.DeleteObjectAsync(_bucket, item.Name, cancellationToken: ct);
        }
    }
}
