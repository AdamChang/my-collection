using FluentValidation;
using MediatR;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Showcase;

/// <summary>首頁精選牆：跨品類混合，只顯示 isShowcased。</summary>
public record GetShowcaseQuery(int Page = 1, int PageSize = 24) : IRequest<PagedResult<ItemDto>>;

public sealed class GetShowcaseQueryValidator : AbstractValidator<GetShowcaseQuery>
{
    public GetShowcaseQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

public sealed class GetShowcaseQueryHandler(IItemRepository items, ICategoryRepository categories)
    : IRequestHandler<GetShowcaseQuery, PagedResult<ItemDto>>
{
    public async Task<PagedResult<ItemDto>> Handle(GetShowcaseQuery request, CancellationToken cancellationToken)
    {
        var result = await items.SearchAsync(
            new ItemQuerySpec { IsShowcased = true, Page = request.Page, PageSize = request.PageSize },
            cancellationToken);

        var displayModes = CategoryMapper.ToDisplayModeLookup(await categories.ListAsync(cancellationToken));

        return new PagedResult<ItemDto>(
            result.Items
                .Select(i => ItemMapper.ToDto(i, displayModes.GetValueOrDefault(i.CategoryId, DisplayMode.List)))
                .ToArray(),
            result.Total,
            result.Page,
            result.PageSize);
    }
}
