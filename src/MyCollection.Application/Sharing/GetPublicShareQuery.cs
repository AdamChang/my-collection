using MediatR;
using MyCollection.Application.Auth;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Sharing;

public record GetPublicShareQuery(string Slug) : IRequest<PublicShareDto>;

/// <summary>
/// 匿名唯讀路徑。不注入 IUserContext，不碰 IItemRepository——
/// 全部走 IPublicCatalogReader 的白名單投影。
/// </summary>
public sealed class GetPublicShareQueryHandler(
    IShareLinkRepository links,
    IPublicCatalogReader catalog,
    IUserRepository users,
    TimeProvider timeProvider) : IRequestHandler<GetPublicShareQuery, PublicShareDto>
{
    public async Task<PublicShareDto> Handle(GetPublicShareQuery request, CancellationToken cancellationToken)
    {
        var link = await links.GetBySlugAsync(request.Slug, cancellationToken)
                   ?? throw new NotFoundException(nameof(ShareLink), request.Slug);

        // 過期連結對外表現得像不存在，不透露曾經存在過
        if (link.ExpiresAt is { } expiresAt && expiresAt <= timeProvider.GetUtcNow().UtcDateTime)
        {
            throw new NotFoundException(nameof(ShareLink), request.Slug);
        }

        var owner = await users.GetByIdAsync(link.OwnerId, cancellationToken);

        var categoryNames = await catalog.ListCategoryNamesAsync(link.OwnerId, cancellationToken);

        var items = await catalog.ListItemsAsync(
            link.OwnerId, link.Scope, link.IncludeCategoryIds, link.IncludePrice, cancellationToken);

        return new PublicShareDto(
            owner?.DisplayName ?? "Collector",
            link.Scope.ToString(),
            items.Select(i => new PublicItemDto(
                i.Id.ToString(),
                i.Name,
                i.Description,
                categoryNames.TryGetValue(i.CategoryId, out var name) ? name : string.Empty,
                i.Tags,
                i.Images
                    .OrderBy(img => img.Order)
                    .Select(img => new PublicImageDto(img.CardPath, img.ThumbPath, img.IsPrimary, img.Order))
                    .ToArray(),
                BsonJson.ToDictionary(i.Attributes),
                i.Price is null ? null : new PublicPriceDto(i.Price.Amount, i.Price.Currency))).ToArray());
    }
}
