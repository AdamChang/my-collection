using System.IO.Compression;
using FluentValidation;
using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Application.Media;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Transfer;

/// <param name="Archive">必須可 seek——ZipArchive 需要隨機存取 central directory。</param>
public record ImportArchiveCommand(Stream Archive) : IRequest<ImportResultDto>;

public sealed record ImportResultDto(
    int Categories,
    int Items,
    int Images,
    IReadOnlyList<string> Warnings);

public sealed class ImportArchiveCommandHandler(
    ITransferRepository repository,
    ICategoryRepository categories,
    ArchiveValidator validator,
    ArchiveWriter archiveWriter,
    IBackupStore backups,
    IFileStorage storage,
    IImageProcessor imageProcessor,
    IUserContext userContext,
    TimeProvider timeProvider) : IRequestHandler<ImportArchiveCommand, ImportResultDto>
{
    private const int BackupsToKeep = 3;

    public async Task<ImportResultDto> Handle(ImportArchiveCommand request, CancellationToken ct)
    {
        using var archive = OpenArchive(request.Archive);
        var manifest = ReadManifest(archive);

        // ---- 階段一：驗證。這一段結束前不寫入任何東西。 ----
        var systemCategories = (await categories.ListAsync(ct)).Where(c => c.OwnerId is null).ToList();
        var failures = validator.Validate(manifest, systemCategories);

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        // ---- 備份。沒有 transaction，這是階段二失敗後唯一的復原手段。 ----
        var ownerId = userContext.UserId;
        var now = timeProvider.GetUtcNow();

        await using (var backup = await backups.CreateAsync(
                         ownerId, $"pre-import-{now:yyyyMMdd-HHmmss}.zip", ct))
        {
            await archiveWriter.WriteAsync(backup, ct);
        }

        await backups.PruneAsync(ownerId, BackupsToKeep, ct);

        // ---- 階段二：套用。 ----
        var warnings = new List<string>();

        var replaced = await repository.ListExportableItemsAsync(ct);
        await repository.DeleteNonSteamItemsAsync(ct);

        foreach (var item in replaced)
        {
            await storage.DeleteDirectoryAsync($"{ownerId}/{item.Id}", ct);
        }

        await repository.DeleteOwnShareLinksAsync(ct);

        var steamItems = await repository.ListSteamItemsAsync(ct);
        var localCategories = await repository.ListOwnCategoriesAsync(ct);
        var plan = CategoryReconciler.Plan(localCategories, manifest.Categories, steamItems);

        // repoint 必須在 delete 之前：否則 Steam item 會在中間狀態指向已不存在的品類。
        foreach (var repoint in plan.Repoints)
        {
            await repository.RepointItemsAsync(repoint.FromCategoryId, repoint.ToCategoryId, ct);
        }

        await repository.DeleteCategoriesAsync(plan.Delete, ct);

        warnings.AddRange(plan.KeptOrphanNames.Select(name =>
            $"品類「{name}」因仍有 Steam 品項掛在上面而保留，未被封存檔取代。"));

        await repository.InsertCategoriesAsync(
            [.. manifest.Categories.Select(c => ArchiveMapper.ToDomain(c, ownerId))], ct);

        var (items, imageCount, imageWarnings) = await BuildItemsAsync(archive, manifest, ownerId, ct);
        warnings.AddRange(imageWarnings);

        await repository.InsertItemsAsync(items, ct);

        var shareLinks = await BuildShareLinksAsync(manifest, ownerId, warnings, ct);
        await repository.InsertShareLinksAsync(shareLinks, ct);

        return new ImportResultDto(manifest.Categories.Count, items.Count, imageCount, warnings);
    }

    /// <summary>
    /// manifest 的大小上限。ArchiveManifestSerializer.Read 的 doc comment 說明了原因：
    /// MongoDB 的 JsonReader 對巢狀深度沒有上限，極深巢狀的 JSON 會觸發無法攔截的
    /// StackOverflowException 直接終止行程，只能在讀取前用大小把它擋掉。
    /// 個人收藏的 manifest 是幾 MB 等級，64 MB 留了非常寬裕的餘裕。
    /// </summary>
    private const long MaxManifestBytes = 64L * 1024 * 1024;

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

    private static ArchiveManifest ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry(ArchiveManifest.FileName)
                    ?? throw new InvalidArchiveException($"封存檔內缺少 {ArchiveManifest.FileName}。");

        if (entry.Length > MaxManifestBytes)
        {
            throw new InvalidArchiveException(
                $"{ArchiveManifest.FileName} 超過 {MaxManifestBytes / 1024 / 1024} MB 上限。");
        }

        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;

        // Read 內部已經把 MongoDB.Bson 會丟的各種例外統一成 InvalidArchiveException，
        // 也已經檢查過 schemaVersion，這裡不需要再包一層。
        return ArchiveManifestSerializer.Read(buffer);
    }

    private async Task<(List<Item> Items, int ImageCount, List<string> Warnings)> BuildItemsAsync(
        ZipArchive archive, ArchiveManifest manifest, ObjectId ownerId, CancellationToken ct)
    {
        var items = new List<Item>(manifest.Items.Count);
        var warnings = new List<string>();
        var imageCount = 0;

        foreach (var source in manifest.Items)
        {
            var item = new Item
            {
                Id = source.Id,
                OwnerId = ownerId,
                CategoryId = source.CategoryId,
                Name = source.Name,
                Description = source.Description,
                Tags = source.Tags,
                IsShowcased = source.IsShowcased,
                Source = source.Source,
                ExternalRef = ArchiveMapper.ToDomain(source.ExternalRef),
                Acquisition = ArchiveMapper.ToDomain(source.Acquisition),
                Attributes = source.Attributes,
                CreatedAt = source.CreatedAt,
                UpdatedAt = source.UpdatedAt
            };

            foreach (var image in source.Images.OrderBy(i => i.Order))
            {
                var entry = archive.GetEntry(image.File);

                if (entry is null)
                {
                    warnings.Add($"品項「{source.Name}」的圖片 {image.File} 不在封存檔內，已略過。");
                    continue;
                }

                await using var content = entry.Open();
                var processed = await imageProcessor.ProcessAsync(content, ct);

                item.Images.Add(new ItemImage
                {
                    Id = image.Id,
                    Path = await SaveAsync(MediaPaths.Full(item, image.Id), processed.Full, ct),
                    CardPath = await SaveAsync(MediaPaths.Card(item, image.Id), processed.Card, ct),
                    ThumbPath = await SaveAsync(MediaPaths.Thumb(item, image.Id), processed.Thumb, ct),
                    IsPrimary = image.IsPrimary,
                    Order = image.Order
                });

                imageCount++;
            }

            // 主圖可能是那張缺檔的圖，補一張回來，避免品項變成沒有主圖。
            if (item.Images.Count > 0 && item.Images.TrueForAll(i => !i.IsPrimary))
            {
                item.Images[0].IsPrimary = true;
            }

            items.Add(item);
        }

        return (items, imageCount, warnings);
    }

    private async Task<string> SaveAsync(string path, byte[] content, CancellationToken ct)
    {
        using var stream = new MemoryStream(content);

        return await storage.SaveAsync(path, stream, ct);
    }

    private async Task<List<ShareLink>> BuildShareLinksAsync(
        ArchiveManifest manifest, ObjectId ownerId, List<string> warnings, CancellationToken ct)
    {
        var links = new List<ShareLink>(manifest.ShareLinks.Count);

        foreach (var source in manifest.ShareLinks)
        {
            var slug = source.Slug;

            if (await repository.SlugExistsAsync(slug, ct))
            {
                slug = ObjectId.GenerateNewId().ToString();
                warnings.Add($"分享連結 {source.Slug} 已被占用，改用 {slug}。");
            }

            links.Add(new ShareLink
            {
                Id = ObjectId.GenerateNewId(),
                OwnerId = ownerId,
                Slug = slug,
                Scope = source.Scope,
                IncludeCategoryIds = source.IncludeCategoryIds,
                IncludePrice = source.IncludePrice,
                ExpiresAt = source.ExpiresAt,
                CreatedAt = source.CreatedAt
            });
        }

        return links;
    }
}
