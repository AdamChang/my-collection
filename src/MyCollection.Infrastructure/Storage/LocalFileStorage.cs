using Microsoft.Extensions.Options;
using MyCollection.Application.Common;

namespace MyCollection.Infrastructure.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IOptions<StorageOptions> options)
    {
        _root = Path.GetFullPath(options.Value.LocalRoot);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct)
    {
        var fullPath = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var target = File.Create(fullPath);
        await content.CopyToAsync(target, ct);

        return relativePath;
    }

    public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct)
    {
        var fullPath = Resolve(relativePath);

        return Task.FromResult<Stream?>(
            File.Exists(fullPath) ? File.OpenRead(fullPath) : null);
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct)
    {
        var fullPath = Resolve(relativePath);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string relativePrefix, CancellationToken ct)
    {
        var fullPath = Resolve(relativePrefix);

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 把相對路徑解析成根目錄底下的絕對路徑，並拒絕任何逃逸嘗試。
    /// 這是唯一的邊界檢查點，所有公開方法都先走過它。
    /// </summary>
    private string Resolve(string relativePath)
    {
        relativePath = StoragePath.Validate(relativePath);

        var candidate = Path.GetFullPath(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!candidate.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Path escapes the storage root.", nameof(relativePath));
        }

        return candidate;
    }
}
