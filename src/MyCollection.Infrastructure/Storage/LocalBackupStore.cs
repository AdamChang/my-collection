using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MyCollection.Application.Common;

namespace MyCollection.Infrastructure.Storage;

public sealed class LocalBackupStore : IBackupStore
{
    private readonly string _root;

    public LocalBackupStore(IOptions<StorageOptions> options)
    {
        _root = Path.GetFullPath(options.Value.BackupRoot);
        Directory.CreateDirectory(_root);
    }

    public Task<Stream> CreateAsync(ObjectId ownerId, string fileName, CancellationToken ct)
    {
        var directory = OwnerDirectory(ownerId);
        Directory.CreateDirectory(directory);

        // fileName 由呼叫端以時間戳組成，不含使用者輸入；仍取 GetFileName 剝掉任何目錄成分。
        var path = Path.Combine(directory, Path.GetFileName(fileName));

        return Task.FromResult<Stream>(File.Create(path));
    }

    public Task PruneAsync(ObjectId ownerId, int keep, CancellationToken ct)
    {
        var directory = OwnerDirectory(ownerId);

        if (!Directory.Exists(directory))
        {
            return Task.CompletedTask;
        }

        // 依檔名排序而非寫入時間：檔名含時間戳，且不受檔案系統時間精度或搬移影響。
        var stale = Directory.GetFiles(directory)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(keep);

        foreach (var file in stale)
        {
            File.Delete(file);
        }

        return Task.CompletedTask;
    }

    private string OwnerDirectory(ObjectId ownerId) => Path.Combine(_root, ownerId.ToString());
}
