using FluentValidation;
using MediatR;

namespace MyCollection.Application.Ingestion;

/// <summary>
/// 關鍵字搜尋外部來源，供前端預填表單，不建立品項。
/// 回傳型別沿用 FetchByUrlQuery 的 FetchedMetadataDto——兩者對前端是同一件事。
/// </summary>
public record SearchProviderQuery(string Provider, string Query, int Limit = 20)
    : IRequest<IReadOnlyList<FetchedMetadataDto>>;

public sealed class SearchProviderQueryValidator : AbstractValidator<SearchProviderQuery>
{
    public SearchProviderQueryValidator()
    {
        RuleFor(x => x.Provider).NotEmpty();

        // 單字元查詢對 IGDB 沒有意義，只會回一堆雜訊
        RuleFor(x => x.Query).NotEmpty().MinimumLength(2);

        RuleFor(x => x.Limit).InclusiveBetween(1, 50);
    }
}

public sealed class SearchProviderQueryHandler(ProviderRegistry registry)
    : IRequestHandler<SearchProviderQuery, IReadOnlyList<FetchedMetadataDto>>
{
    public async Task<IReadOnlyList<FetchedMetadataDto>> Handle(
        SearchProviderQuery request, CancellationToken cancellationToken)
    {
        var provider = registry.Require<ISearchProvider>(request.Provider);

        var items = await provider.SearchAsync(request.Query, request.Limit, cancellationToken);

        return items.Select(item => new FetchedMetadataDto(
            provider.Key,
            item.ExternalId,
            item.Name,
            item.Description,
            item.ImageUrl?.ToString(),
            item.Attributes)).ToArray();
    }
}
