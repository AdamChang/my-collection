using MediatR;
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
    TimeProvider timeProvider) : IRequestHandler<UploadItemImageCommand, ItemImageDto>
{
    public async Task<ItemImageDto> Handle(UploadItemImageCommand request, CancellationToken cancellationToken)
    {
        var item = await LoadItemAsync(items, request.ItemId, cancellationToken);

        var processed = await imageProcessor.ProcessAsync(request.Content, cancellationToken);
        var imageId = ObjectId.GenerateNewId().ToString();

        var image = new ItemImage
        {
            Id = imageId,
            Path = await SaveAsync(storage, MediaPaths.Full(item, imageId), processed.Full, cancellationToken),
            CardPath = await SaveAsync(storage, MediaPaths.Card(item, imageId), processed.Card, cancellationToken),
            ThumbPath = await SaveAsync(storage, MediaPaths.Thumb(item, imageId), processed.Thumb, cancellationToken),
            IsPrimary = item.Images.Count == 0,
            Order = item.Images.Count
        };

        item.Images.Add(image);
        item.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await items.UpdateAsync(item, cancellationToken);

        return new ItemImageDto(image.Id, image.Path, image.CardPath, image.ThumbPath, image.IsPrimary, image.Order);
    }

    private static async Task<string> SaveAsync(IFileStorage storage, string path, byte[] content, CancellationToken ct)
    {
        using var stream = new MemoryStream(content);
        return await storage.SaveAsync(path, stream, ct);
    }

    internal static async Task<Item> LoadItemAsync(IItemRepository items, string itemId, CancellationToken ct)
    {
        if (!ObjectId.TryParse(itemId, out var id))
        {
            throw new NotFoundException(nameof(Item), itemId);
        }

        return await items.GetAsync(id, ct) ?? throw new NotFoundException(nameof(Item), itemId);
    }
}

public sealed class DeleteItemImageCommandHandler(
    IItemRepository items,
    IFileStorage storage,
    TimeProvider timeProvider) : IRequestHandler<DeleteItemImageCommand>
{
    public async Task Handle(DeleteItemImageCommand request, CancellationToken cancellationToken)
    {
        var item = await UploadItemImageCommandHandler.LoadItemAsync(items, request.ItemId, cancellationToken);

        var image = item.Images.SingleOrDefault(i => i.Id == request.ImageId)
                    ?? throw new NotFoundException(nameof(ItemImage), request.ImageId);

        await storage.DeleteAsync(image.Path, cancellationToken);
        await storage.DeleteAsync(image.CardPath, cancellationToken);
        await storage.DeleteAsync(image.ThumbPath, cancellationToken);

        item.Images.Remove(image);

        // 主圖被刪掉時晉升下一張，並重排 order
        if (image.IsPrimary && item.Images.Count > 0)
        {
            item.Images[0].IsPrimary = true;
        }

        for (var i = 0; i < item.Images.Count; i++)
        {
            item.Images[i].Order = i;
        }

        item.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await items.UpdateAsync(item, cancellationToken);
    }
}

public sealed class SetPrimaryImageCommandHandler(IItemRepository items, TimeProvider timeProvider)
    : IRequestHandler<SetPrimaryImageCommand>
{
    public async Task Handle(SetPrimaryImageCommand request, CancellationToken cancellationToken)
    {
        var item = await UploadItemImageCommandHandler.LoadItemAsync(items, request.ItemId, cancellationToken);

        if (item.Images.All(i => i.Id != request.ImageId))
        {
            throw new NotFoundException(nameof(ItemImage), request.ImageId);
        }

        foreach (var image in item.Images)
        {
            image.IsPrimary = image.Id == request.ImageId;
        }

        item.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await items.UpdateAsync(item, cancellationToken);
    }
}
