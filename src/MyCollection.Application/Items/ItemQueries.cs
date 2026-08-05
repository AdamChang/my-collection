using FluentValidation;
using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Items;

public record SearchItemsQuery(
    string? Search = null,
    string? CategoryId = null,
    IReadOnlyList<string>? Tags = null,
    bool? IsShowcased = null,
    int Page = 1,
    int PageSize = 24,
    IReadOnlyDictionary<string, string>? Attributes = null) : IRequest<PagedResult<ItemDto>>;

public record GetItemQuery(string Id) : IRequest<ItemDto>;

public record ListTagsQuery : IRequest<IReadOnlyList<string>>;

public record ListPlatformsQuery(string? CategoryId = null) : IRequest<IReadOnlyList<string>>;

public sealed class ListPlatformsQueryValidator : AbstractValidator<ListPlatformsQuery>
{
    public ListPlatformsQueryValidator()
    {
        RuleFor(x => x.CategoryId)
            .Must(id => ObjectId.TryParse(id, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.CategoryId))
            .WithMessage("Invalid category id.");
    }
}

public sealed class SearchItemsQueryValidator : AbstractValidator<SearchItemsQuery>
{
    public SearchItemsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.CategoryId)
            .Must(id => ObjectId.TryParse(id, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.CategoryId))
            .WithMessage("Invalid category id.");
    }
}

public sealed class SearchItemsQueryHandler(IItemRepository items)
    : IRequestHandler<SearchItemsQuery, PagedResult<ItemDto>>
{
    public async Task<PagedResult<ItemDto>> Handle(SearchItemsQuery request, CancellationToken cancellationToken)
    {
        var spec = new ItemQuerySpec
        {
            Search = request.Search,
            CategoryId = string.IsNullOrWhiteSpace(request.CategoryId) ? null : ObjectId.Parse(request.CategoryId),
            Tags = request.Tags,
            IsShowcased = request.IsShowcased,
            Page = request.Page,
            PageSize = request.PageSize,
            Attributes = request.Attributes
        };

        var result = await items.SearchAsync(spec, cancellationToken);

        return new PagedResult<ItemDto>(
            result.Items.Select(ItemMapper.ToDto).ToArray(),
            result.Total,
            result.Page,
            result.PageSize);
    }
}

public sealed class GetItemQueryHandler(IItemRepository items) : IRequestHandler<GetItemQuery, ItemDto>
{
    public async Task<ItemDto> Handle(GetItemQuery request, CancellationToken cancellationToken)
    {
        // 不合法的 id 是「找不到」而非伺服器錯誤；直接 Parse 會擲 FormatException 變成 500
        if (!ObjectId.TryParse(request.Id, out var id))
        {
            throw new NotFoundException(nameof(Item), request.Id);
        }

        var item = await items.GetAsync(id, cancellationToken)
                   ?? throw new NotFoundException(nameof(Item), request.Id);

        return ItemMapper.ToDto(item);
    }
}

public sealed class ListTagsQueryHandler(IItemRepository items) : IRequestHandler<ListTagsQuery, IReadOnlyList<string>>
{
    public Task<IReadOnlyList<string>> Handle(ListTagsQuery request, CancellationToken cancellationToken) =>
        items.ListTagsAsync(cancellationToken);
}

public sealed class ListPlatformsQueryHandler(IItemRepository items)
    : IRequestHandler<ListPlatformsQuery, IReadOnlyList<string>>
{
    public Task<IReadOnlyList<string>> Handle(ListPlatformsQuery request, CancellationToken cancellationToken)
    {
        var categoryId = string.IsNullOrWhiteSpace(request.CategoryId) ? null : (ObjectId?)ObjectId.Parse(request.CategoryId);

        return items.ListPlatformsAsync(categoryId, cancellationToken);
    }
}
