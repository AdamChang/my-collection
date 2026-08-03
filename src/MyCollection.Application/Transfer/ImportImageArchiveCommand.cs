using System.IO.Compression;
using MediatR;
using MyCollection.Application.Common;

namespace MyCollection.Application.Transfer;

/// <param name="Archive">必須可 seek——ZipArchive 需要隨機存取 central directory。</param>
public record ImportImageArchiveCommand(Stream Archive) : IRequest<ImageImportResultDto>;

public sealed record ImageImportResultDto(int Written, int Skipped, IReadOnlyList<string> Warnings);

/// <summary>
/// 匯入 = 把 zip 內的圖檔寫回本地 storage，僅此而已。
///
/// 不碰 MongoDB：資料庫是共用的（Atlas），DB 裡的 ItemImage 路徑在每台機器上
/// 本來就一樣，缺的只是檔案本身。也因此這個操作沒有破壞性，不需要事前備份。
/// </summary>
public sealed class ImportImageArchiveCommandHandler(
    IFileStorage storage,
    IUserContext userContext) : IRequestHandler<ImportImageArchiveCommand, ImageImportResultDto>
{
    /// <summary>缺檔警告最多列這麼多筆，其餘併成一句，避免一份壞掉的封存檔洗版。</summary>
    private const int MaxListedWarnings = 20;

    public async Task<ImageImportResultDto> Handle(ImportImageArchiveCommand request, CancellationToken ct)
    {
        using var archive = OpenArchive(request.Archive);
        var manifest = ReadManifest(archive);

        var ownerId = userContext.UserId.ToString();

        if (!string.Equals(manifest.OwnerId, ownerId, StringComparison.Ordinal))
        {
            throw new InvalidArchiveException(
                "這份封存檔屬於其他帳號，無法匯入。共用同一個資料庫時，"
                + "同一個帳號在每台機器上的 id 會是相同的；不同就代表拿錯了檔案。");
        }

        // 先整包驗證再寫入：任何一條路徑不合格就整包拒絕，不留半套。
        var images = CollectImageEntries(archive, ownerId);

        var written = 0;
        var skipped = 0;

        foreach (var entry in images)
        {
            if (await ExistsAsync(entry.FullName, ct))
            {
                // 路徑含 imageId，而 imageId 每次上傳都是新的 ObjectId——
                // 同一個路徑不可能合法地裝著不同內容，所以略過即正確。
                skipped++;
                continue;
            }

            await using var content = entry.Open();
            await storage.SaveAsync(entry.FullName, content, ct);
            written++;
        }

        return new ImageImportResultDto(written, skipped, BuildWarnings(manifest));
    }

    private static ZipArchive OpenArchive(Stream source)
    {
        try
        {
            return new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidArchiveException("上傳的檔案不是合法的 ZIP 封存檔。", exception);
        }
    }

    private static ImageArchiveManifest ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry(ImageArchiveManifest.FileName)
                    ?? throw new InvalidArchiveException($"封存檔內缺少 {ImageArchiveManifest.FileName}。");

        if (entry.Length > ImageArchiveManifestSerializer.MaxBytes)
        {
            throw new InvalidArchiveException(
                $"{ImageArchiveManifest.FileName} 超過 {ImageArchiveManifestSerializer.MaxBytes / 1024} KB 上限。");
        }

        using var stream = entry.Open();

        // Read 內部已經把解析失敗與版本不符統一成 InvalidArchiveException。
        return ImageArchiveManifestSerializer.Read(stream);
    }

    /// <summary>
    /// 收集要寫入的圖檔 entry，順便當成寫入前的整包驗證。
    ///
    /// ownerId 前綴一定要在這裡擋：IFileStorage 只保證不寫出 storage root 之外，
    /// 不保證不寫進「別人的」目錄，少了這一關，任何已登入者都能構造一個 zip
    /// 覆蓋別人的圖檔。
    /// </summary>
    private static List<ZipArchiveEntry> CollectImageEntries(ZipArchive archive, string ownerId)
    {
        var prefix = ownerId + "/";
        var images = new List<ZipArchiveEntry>(archive.Entries.Count);

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName == ImageArchiveManifest.FileName || entry.FullName.EndsWith('/'))
            {
                continue;
            }

            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal)
                || !entry.FullName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                || entry.FullName.Split('/').Contains(".."))
            {
                throw new InvalidArchiveException($"封存檔內含不該出現的路徑：{entry.FullName}");
            }

            images.Add(entry);
        }

        return images;
    }

    private async Task<bool> ExistsAsync(string path, CancellationToken ct)
    {
        await using var existing = await storage.OpenReadAsync(path, ct);

        return existing is not null;
    }

    /// <summary>
    /// 缺檔是匯出端當下就發現的事實（DB 有記錄、磁碟上沒檔），匯入端無力補救，
    /// 但必須說出來——否則使用者只會在某天看到破圖才發現圖早就掉了。
    /// </summary>
    private static IReadOnlyList<string> BuildWarnings(ImageArchiveManifest manifest)
    {
        if (manifest.Missing.Count == 0)
        {
            return [];
        }

        var warnings = manifest.Missing
            .Take(MaxListedWarnings)
            .Select(m => $"匯出來源缺少「{m.ItemName}」的圖檔 {m.Path}，這台機器上也不會有。")
            .ToList();

        if (manifest.Missing.Count > MaxListedWarnings)
        {
            warnings.Add($"另有 {manifest.Missing.Count - MaxListedWarnings} 個圖檔在匯出來源上就已遺失。");
        }

        return warnings;
    }
}
