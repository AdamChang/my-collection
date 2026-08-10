using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Application.Sharing;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Media;

public sealed record MediaContent(Stream Content, string ContentType);

public sealed record OpenOwnedMediaQuery(string Path) : IRequest<MediaContent>;

public sealed record OpenPublicMediaQuery(string Slug, string Path) : IRequest<MediaContent>;

public sealed class OpenOwnedMediaQueryHandler(IItemRepository items, IFileStorage storage)
    : IRequestHandler<OpenOwnedMediaQuery, MediaContent>
{
    public async Task<MediaContent> Handle(OpenOwnedMediaQuery request, CancellationToken cancellationToken)
    {
        var segments = request.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 3 || !ObjectId.TryParse(segments[1], out var itemId))
        {
            throw NotFound(request.Path);
        }

        var item = await items.GetAsync(itemId, cancellationToken);
        if (item is null || !ContainsPath(item.Images, request.Path))
        {
            throw NotFound(request.Path);
        }

        return await OpenAsync(storage, request.Path, cancellationToken);
    }

    internal static bool ContainsPath(IEnumerable<ItemImage> images, string path) =>
        images.Any(image =>
            string.Equals(image.Path, path, StringComparison.Ordinal) ||
            string.Equals(image.CardPath, path, StringComparison.Ordinal) ||
            string.Equals(image.ThumbPath, path, StringComparison.Ordinal));

    internal static async Task<MediaContent> OpenAsync(
        IFileStorage storage,
        string path,
        CancellationToken cancellationToken)
    {
        if (!path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
        {
            throw NotFound(path);
        }

        Stream? content;
        try
        {
            content = await storage.OpenReadAsync(path, cancellationToken);
        }
        catch (ArgumentException)
        {
            throw NotFound(path);
        }

        return content is null
            ? throw NotFound(path)
            : new MediaContent(content, "image/webp");
    }

    private static NotFoundException NotFound(string path) => new(nameof(ItemImage), path);
}

public sealed class OpenPublicMediaQueryHandler(
    IShareLinkRepository links,
    IPublicCatalogReader catalog,
    IFileStorage storage,
    TimeProvider timeProvider) : IRequestHandler<OpenPublicMediaQuery, MediaContent>
{
    public async Task<MediaContent> Handle(OpenPublicMediaQuery request, CancellationToken cancellationToken)
    {
        var link = await links.GetBySlugAsync(request.Slug, cancellationToken);
        if (link is null || link.ExpiresAt is { } expiresAt && expiresAt <= timeProvider.GetUtcNow().UtcDateTime)
        {
            throw new NotFoundException(nameof(ShareLink), request.Slug);
        }

        var sharedItems = await catalog.ListItemsAsync(
            link.OwnerId,
            link.Scope,
            link.IncludeCategoryIds,
            includePrice: false,
            includeRating: false,
            cancellationToken);

        if (!sharedItems.Any(item => OpenOwnedMediaQueryHandler.ContainsPath(item.Images, request.Path)))
        {
            throw new NotFoundException(nameof(ItemImage), request.Path);
        }

        return await OpenOwnedMediaQueryHandler.OpenAsync(storage, request.Path, cancellationToken);
    }
}
