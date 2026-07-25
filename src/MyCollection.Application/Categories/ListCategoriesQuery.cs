using MediatR;

namespace MyCollection.Application.Categories;

public record ListCategoriesQuery : IRequest<IReadOnlyList<CategoryDto>>;

public sealed class ListCategoriesQueryHandler(ICategoryRepository repository)
    : IRequestHandler<ListCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    public async Task<IReadOnlyList<CategoryDto>> Handle(ListCategoriesQuery request, CancellationToken cancellationToken)
    {
        var categories = await repository.ListAsync(cancellationToken);

        return categories.Select(CategoryMapper.ToDto).ToArray();
    }
}
