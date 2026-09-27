using MediatR;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Media;

public record UploadItemImageCommand(string ItemId, Stream Content) : IRequest<ItemImageDto>;

public record DeleteItemImageCommand(string ItemId, string ImageId) : IRequest;

public record SetPrimaryImageCommand(string ItemId, string ImageId) : IRequest;

internal static class MediaPaths
{
    public static string Full(Item item, string imageId) => $"{item.OwnerId}/{item.Id}/{imageId}-full.webp";

    public static string Card(Item item, string imageId) => $"{item.OwnerId}/{item.Id}/{imageId}-card.webp";

    public static string Thumb(Item item, string imageId) => $"{item.OwnerId}/{item.Id}/{imageId}-thumb.webp";
}

public sealed class UploadItemImageCommandHandler(
    IItemRepository items,
    IFileStorage storage,
    IImageProcessor imageProcessor,
    TimeProvider timeProvider,
    ILogger<UploadItemImageCommandHandler> logger) : IRequestHandler<UploadItemImageCommand, ItemImageDto>
{
    public async Task<ItemImageDto> Handle(UploadItemImageCommand request, CancellationToken cancellationToken)
    {
        // 先讀一次：檔案路徑需要 OwnerId，而且品項不存在時不該白做影像處理。
        // 讀到的 images 不參與寫入——主圖與順序由 AddImageAsync 在資料庫端決定（M1）。
        var item = await LoadItemAsync(items, request.ItemId, cancellationToken);

        var processed = await imageProcessor.ProcessAsync(request.Content, cancellationToken);
        var imageId = ObjectId.GenerateNewId().ToString();

        var image = new ItemImage
        {
            Id = imageId,
            Path = await SaveAsync(storage, MediaPaths.Full(item, imageId), processed.Full, cancellationToken),
            CardPath = await SaveAsync(storage, MediaPaths.Card(item, imageId), processed.Card, cancellationToken),
            ThumbPath = await SaveAsync(storage, MediaPaths.Thumb(item, imageId), processed.Thumb, cancellationToken)
        };

        ItemImage saved;
        try
        {
            saved = await items.AddImageAsync(
                item.Id, image, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        }
        catch
        {
            // 檔案已寫入但 DB 沒有引用（品項在處理期間被刪、或寫入失敗）：補償刪檔，否則就是孤兒檔（M6）。
            // 原始例外照常往外拋，由 GlobalExceptionHandler 轉換。
            await DeleteFilesQuietlyAsync(storage, image, logger, CancellationToken.None);
            throw;
        }

        return new ItemImageDto(saved.Id, saved.Path, saved.CardPath, saved.ThumbPath, saved.IsPrimary, saved.Order);
    }

    private static async Task<string> SaveAsync(IFileStorage storage, string path, byte[] content, CancellationToken ct)
    {
        using var stream = new MemoryStream(content);
        return await storage.SaveAsync(path, stream, ct);
    }

    /// <summary>
    /// 盡力刪除一張圖的三個檔案。刪不掉只留下孤兒檔（佔空間、不影響正確性），
    /// 不可以蓋掉呼叫端真正要回報的結果或例外，所以記 log 後吞掉。
    /// </summary>
    internal static async Task DeleteFilesQuietlyAsync(
        IFileStorage storage, ItemImage image, ILogger logger, CancellationToken ct)
    {
        foreach (var path in new[] { image.Path, image.CardPath, image.ThumbPath })
        {
            try
            {
                await storage.DeleteAsync(path, ct);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to delete image file {Path}; it is now an orphan.", path);
            }
        }
    }

    internal static async Task<Item> LoadItemAsync(IItemRepository items, string itemId, CancellationToken ct)
    {
        if (!ObjectId.TryParse(itemId, out var id))
        {
            throw new NotFoundException(nameof(Item), itemId);
        }

        return await items.GetAsync(id, ct) ?? throw new NotFoundException(nameof(Item), itemId);
    }

    internal static ObjectId ParseItemId(string itemId) =>
        ObjectId.TryParse(itemId, out var id) ? id : throw new NotFoundException(nameof(Item), itemId);
}

public sealed class DeleteItemImageCommandHandler(
    IItemRepository items,
    IFileStorage storage,
    TimeProvider timeProvider,
    ILogger<DeleteItemImageCommandHandler> logger) : IRequestHandler<DeleteItemImageCommand>
{
    public async Task Handle(DeleteItemImageCommand request, CancellationToken cancellationToken)
    {
        // 先改 DB 再刪檔（M6）：反過來的話，DB 寫入失敗會留下指向已刪檔案的破圖；
        // 這個順序失敗時最多只留下孤兒檔。
        var removed = await items.RemoveImageAsync(
            UploadItemImageCommandHandler.ParseItemId(request.ItemId),
            request.ImageId,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await UploadItemImageCommandHandler.DeleteFilesQuietlyAsync(storage, removed, logger, cancellationToken);
    }
}

public sealed class SetPrimaryImageCommandHandler(IItemRepository items, TimeProvider timeProvider)
    : IRequestHandler<SetPrimaryImageCommand>
{
    public Task Handle(SetPrimaryImageCommand request, CancellationToken cancellationToken) =>
        items.SetPrimaryImageAsync(
            UploadItemImageCommandHandler.ParseItemId(request.ItemId),
            request.ImageId,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
}
