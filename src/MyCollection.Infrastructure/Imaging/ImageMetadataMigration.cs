using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyCollection.Application.Auth;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Application.Media;
using MyCollection.Domain.Entities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace MyCollection.Infrastructure.Imaging;

public sealed record ImageMetadataMigrationReport(
    int Total,
    int Clean,
    int NeedsStripping,
    int WithGps,
    int Missing,
    int Failed)
{
    public bool HasProblems => Missing > 0 || Failed > 0;
}

/// <summary>
/// 一次性維運：清除 M2 修正前已上傳圖片的 EXIF／XMP／IPTC。驗收後即移除。
/// 原檔未保存，因此以 full 為來源交給 <see cref="IImageProcessor"/> 重產三個尺寸、原路徑覆寫；
/// 路徑不變所以不寫 Mongo，也不會與品項編輯互相覆蓋（C2）。
/// </summary>
public sealed class ImageMetadataMigration(
    IServiceScopeFactory scopeFactory,
    ILogger<ImageMetadataMigration> logger)
{
    public const int PageSize = 200;

    private enum Outcome { Clean, NeedsStripping, NeedsStrippingWithGps, Missing, Failed }

    public async Task<ImageMetadataMigrationReport> RunAsync(bool apply, CancellationToken ct)
    {
        IReadOnlyList<MongoDB.Bson.ObjectId> ownerIds;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            ownerIds = await scope.ServiceProvider.GetRequiredService<IUserRepository>().ListIdsAsync(ct);
        }

        var outcomes = new List<Outcome>();
        foreach (var ownerId in ownerIds)
        {
            // 每位使用者一個 scope，品項經由 repository 的 owner filter 列出，不繞過授權模型
            await using var scope = scopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<BackgroundUserContext>().Set(ownerId);
            var items = scope.ServiceProvider.GetRequiredService<IItemRepository>();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            var processor = scope.ServiceProvider.GetRequiredService<IImageProcessor>();

            for (var page = 1; ; page++)
            {
                var result = await items.SearchAsync(new ItemQuerySpec { Page = page, PageSize = PageSize }, ct);
                foreach (var image in result.Items.SelectMany(item => item.Images))
                {
                    outcomes.Add(await MigrateAsync(image, storage, processor, apply, ct));
                }

                if ((long)page * PageSize >= result.Total || result.Items.Count == 0)
                {
                    break;
                }
            }
        }

        var report = new ImageMetadataMigrationReport(
            Total: outcomes.Count,
            Clean: outcomes.Count(o => o == Outcome.Clean),
            NeedsStripping: outcomes.Count(o => o is Outcome.NeedsStripping or Outcome.NeedsStrippingWithGps),
            WithGps: outcomes.Count(o => o == Outcome.NeedsStrippingWithGps),
            Missing: outcomes.Count(o => o == Outcome.Missing),
            Failed: outcomes.Count(o => o == Outcome.Failed));

        logger.LogInformation("Image metadata migration ({Mode}) finished: {Report}", apply ? "apply" : "dry-run", report);
        return report;
    }

    private async Task<Outcome> MigrateAsync(
        ItemImage image,
        IFileStorage storage,
        IImageProcessor processor,
        bool apply,
        CancellationToken ct)
    {
        using var buffer = new MemoryStream();

        // 讀完立刻關閉：稍後要以同一路徑覆寫，本機儲存在 Windows 上會因檔案仍開著而拒絕寫入
        await using (var stored = await storage.OpenReadAsync(image.Path, ct))
        {
            if (stored is null)
            {
                logger.LogWarning("Image {Path} is missing from storage.", image.Path);
                return Outcome.Missing;
            }

            await stored.CopyToAsync(buffer, ct);
        }

        // 單張失敗不中止整批：記錄後繼續，最後由報告的非零 exit code 呈現
        try
        {
            buffer.Position = 0;
            var metadata = (await Image.IdentifyAsync(buffer, ct)).Metadata;
            if (metadata.ExifProfile is null && metadata.XmpProfile is null && metadata.IptcProfile is null)
            {
                return Outcome.Clean;
            }

            var hasGps = metadata.ExifProfile?.TryGetValue(ExifTag.GPSLatitude, out _) == true;

            if (apply)
            {
                buffer.Position = 0;
                var processed = await processor.ProcessAsync(buffer, ct);

                // full 最後寫：「full 已乾淨」是跳過的依據，中途失敗重跑時才不會漏掉 card／thumb
                await SaveAsync(storage, image.CardPath, processed.Card, ct);
                await SaveAsync(storage, image.ThumbPath, processed.Thumb, ct);
                await SaveAsync(storage, image.Path, processed.Full, ct);
            }

            return hasGps ? Outcome.NeedsStrippingWithGps : Outcome.NeedsStripping;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to strip metadata from {Path}.", image.Path);
            return Outcome.Failed;
        }
    }

    private static async Task SaveAsync(IFileStorage storage, string path, byte[] content, CancellationToken ct)
    {
        using var stream = new MemoryStream(content);
        await storage.SaveAsync(path, stream, ct);
    }
}
