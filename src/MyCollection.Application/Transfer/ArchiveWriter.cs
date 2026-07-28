using System.IO.Compression;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Transfer;

/// <summary>
/// 匯出核心。寫入任意 Stream，因此匯出端點（HttpResponse.Body）與
/// 匯入前的自動備份（備份檔）可以共用同一份邏輯。
///
/// 單趟串流，不落暫存檔也不整包進記憶體，所以耗用與收藏規模無關。
/// </summary>
public sealed class ArchiveWriter(
    ITransferRepository repository,
    IFileStorage storage,
    TimeProvider timeProvider)
{
    public async Task WriteAsync(Stream destination, CancellationToken ct)
    {
        var categories = await repository.ListOwnCategoriesAsync(ct);
        var items = await repository.ListExportableItemsAsync(ct);
        var shareLinks = await repository.ListOwnShareLinksAsync(ct);

        var manifest = new ArchiveManifest
        {
            ExportedAt = timeProvider.GetUtcNow().UtcDateTime,
            Categories = [.. categories.Select(ArchiveMapper.ToArchive)],
            Items = [.. items.Select(ArchiveMapper.ToArchive)],
            ShareLinks = [.. shareLinks.Select(ArchiveMapper.ToArchive)]
        };

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        await using (var manifestEntry = archive.CreateEntry(ArchiveManifest.FileName).Open())
        {
            ArchiveManifestSerializer.Write(manifestEntry, manifest);
        }

        foreach (var item in items)
        {
            foreach (var image in item.Images)
            {
                // 檔案遺失不由匯出端處理：manifest 照 DB 寫，zip 內少一個 entry，
                // 由匯入端偵測並降級為 warning。這讓匯出維持單趟串流，
                // 不必為了預檢而把每個檔案開兩次。
                await using var source = await storage.OpenReadAsync(image.Path, ct);
                if (source is null)
                {
                    continue;
                }

                await using var entry = archive.CreateEntry(ArchivePaths.Image(item.Id, image.Id)).Open();
                await source.CopyToAsync(entry, ct);
            }
        }
    }

}
