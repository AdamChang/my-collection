using System.Security.Cryptography;
using FluentValidation;
using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Sharing;

public record CreateShareLinkCommand(
    string Scope,
    IReadOnlyList<string> IncludeCategoryIds,
    bool IncludePrice,
    DateTime? ExpiresAt,
    bool IncludeRating = false,
    int CollageSlotCount = 4) : IRequest<ShareLinkDto>;

public record ListShareLinksQuery : IRequest<IReadOnlyList<ShareLinkDto>>;

public record DeleteShareLinkCommand(string Id) : IRequest;

public sealed class CreateShareLinkCommandValidator : AbstractValidator<CreateShareLinkCommand>
{
    public CreateShareLinkCommandValidator()
    {
        RuleFor(x => x.Scope)
            .Must(s => Enum.TryParse<ShareScope>(s, ignoreCase: true, out _))
            .WithMessage("Scope must be 'Showcase' or 'Category'.");

        RuleFor(x => x.IncludeCategoryIds)
            .NotEmpty()
            .When(x => string.Equals(x.Scope, nameof(ShareScope.Category), StringComparison.OrdinalIgnoreCase))
            .WithMessage("Category scope requires at least one category.");

        RuleForEach(x => x.IncludeCategoryIds)
            .Must(id => ObjectId.TryParse(id, out _))
            .WithMessage("Invalid category id.");

        RuleFor(x => x.CollageSlotCount).InclusiveBetween(1, 10);
    }
}

public static class ShareMapper
{
    public static ShareLinkDto ToDto(ShareLink link) => new(
        link.Id.ToString(),
        link.Slug,
        link.Scope.ToString(),
        link.IncludeCategoryIds.Select(id => id.ToString()).ToArray(),
        link.IncludePrice,
        link.IncludeRating,
        link.CollageSlotCount,
        link.ExpiresAt,
        link.CreatedAt);
}

public sealed class CreateShareLinkCommandHandler(IShareLinkRepository links, TimeProvider timeProvider)
    : IRequestHandler<CreateShareLinkCommand, ShareLinkDto>
{
    private const string SlugAlphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int SlugLength = 12;

    public async Task<ShareLinkDto> Handle(CreateShareLinkCommand request, CancellationToken cancellationToken)
    {
        var link = new ShareLink
        {
            Id = ObjectId.GenerateNewId(),
            Slug = GenerateSlug(),
            Scope = Enum.Parse<ShareScope>(request.Scope, ignoreCase: true),
            IncludeCategoryIds = request.IncludeCategoryIds.Select(ObjectId.Parse).ToList(),
            IncludePrice = request.IncludePrice,
            IncludeRating = request.IncludeRating,
            CollageSlotCount = request.CollageSlotCount,
            // 沒帶 Z 的輸入視為 UTC；資料層會拒絕非 UTC 值
            ExpiresAt = UtcDate.Normalise(request.ExpiresAt),
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };

        await links.InsertAsync(link, cancellationToken);

        return ShareMapper.ToDto(link);
    }

    private static string GenerateSlug() =>
        RandomNumberGenerator.GetString(SlugAlphabet, SlugLength);
}

public sealed class ListShareLinksQueryHandler(IShareLinkRepository links)
    : IRequestHandler<ListShareLinksQuery, IReadOnlyList<ShareLinkDto>>
{
    public async Task<IReadOnlyList<ShareLinkDto>> Handle(ListShareLinksQuery request, CancellationToken cancellationToken)
    {
        var result = await links.ListAsync(cancellationToken);

        return result.Select(ShareMapper.ToDto).ToArray();
    }
}

public sealed class DeleteShareLinkCommandHandler(IShareLinkRepository links) : IRequestHandler<DeleteShareLinkCommand>
{
    public Task Handle(DeleteShareLinkCommand request, CancellationToken cancellationToken)
    {
        // 路由參數沒有 validator 把關，ObjectId.Parse 會擲 FormatException → 500。
        // 非法 id 語意上就是「找不到」。
        if (!ObjectId.TryParse(request.Id, out var id))
        {
            throw new NotFoundException(nameof(ShareLink), request.Id);
        }

        return links.DeleteAsync(id, cancellationToken);
    }
}
