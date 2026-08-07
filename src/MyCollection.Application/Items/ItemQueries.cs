using FluentValidation;
using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Categories;
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
    IReadOnlyDictionary<string, string>? Attributes = null,
    IReadOnlyList<string>? MissingAttributes = null) : IRequest<PagedResult<ItemDto>>;

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

public sealed class SearchItemsQueryHandler(IItemRepository items, ICategoryRepository categories)
    : IRequestHandler<SearchItemsQuery, PagedResult<ItemDto>>
{
    public async Task<PagedResult<ItemDto>> Handle(SearchItemsQuery request, CancellationToken cancellationToken)
    {
        // 品類清單本來就要拿來組 displayMode；「未設定」的品類限縮沿用同一份，不多打一次。
        var allCategories = await categories.ListAsync(cancellationToken);
        var missing = request.MissingAttributes?.Where(k => !string.IsNullOrWhiteSpace(k)).ToArray();

        var spec = new ItemQuerySpec
        {
            Search = request.Search,
            CategoryId = string.IsNullOrWhiteSpace(request.CategoryId) ? null : ObjectId.Parse(request.CategoryId),
            Tags = request.Tags,
            IsShowcased = request.IsShowcased,
            Page = request.Page,
            PageSize = request.PageSize,
            Attributes = request.Attributes,
            MissingAttributes = missing,
            CategoryIds = DeclaringCategoryIds(allCategories, missing)
        };

        var result = await items.SearchAsync(spec, cancellationToken);
        var displayModes = CategoryMapper.ToDisplayModeLookup(allCategories);

        return new PagedResult<ItemDto>(
            result.Items
                .Select(i => ItemMapper.ToDto(i, displayModes.GetValueOrDefault(i.CategoryId, DisplayMode.List)))
                .ToArray(),
            result.Total,
            result.Page,
            result.PageSize);
    }

    /// <summary>
    /// 「未設定 X」只在有宣告 X 的品類裡才有意義——沒宣告 X 的品類，其品項字面上全都「未設定 X」，
    /// 混進來會讓這個篩選失去用途。判定依據是 schema 宣告而非品類身分，見 docs/adr/0006。
    /// 多個 key 時取交集（宣告了全部 key 的品類），與篩選條件本身的 AND 語意一致。
    /// 沒有任何品類宣告時回空清單——語意是「回零筆」，不是「不限縮」。
    /// </summary>
    private static IReadOnlyList<ObjectId>? DeclaringCategoryIds(
        IEnumerable<Category> categories,
        IReadOnlyList<string>? missingKeys) =>
        missingKeys is { Count: > 0 }
            ? categories
                .Where(c => missingKeys.All(key => c.Fields.Any(f => f.Key == key)))
                .Select(c => c.Id)
                .ToArray()
            : null;
}

public sealed class GetItemQueryHandler(IItemRepository items, ICategoryRepository categories)
    : IRequestHandler<GetItemQuery, ItemDto>
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

        // 品類正常一定存在（Item 寫入時就驗證過）；查不到時退回 List 只是防呆，不代表預期狀態
        var category = await categories.GetAsync(item.CategoryId, cancellationToken);

        return ItemMapper.ToDto(item, category?.DefaultDisplayMode ?? DisplayMode.List);
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
