using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Categories;

/// <summary>
/// 系統的遊戲品類已在 SystemCategoryDefinitions 內建 provider 欄位，不需要這組端點。
/// 這裡服務的是使用者自訂品類想接上 provider 的情境。
/// </summary>
public record MissingProviderFieldsQuery(string CategoryId, string Provider)
    : IRequest<IReadOnlyList<CategoryFieldDto>>;

public record EnsureProviderFieldsCommand(string CategoryId, string Provider) : IRequest<CategoryDto>;

internal static class ProviderFields
{
    public static async Task<(Category Category, IReadOnlyList<CategoryField> Missing)> ResolveAsync(
        ProviderRegistry registry, ICategoryRepository categories,
        string categoryId, string providerKey, CancellationToken ct)
    {
        var provider = registry.Require<IExternalIdLookupProvider>(providerKey);

        if (!ObjectId.TryParse(categoryId, out var id))
        {
            throw new NotFoundException("Category", categoryId);
        }

        var category = await categories.GetAsync(id, ct)
                       ?? throw new NotFoundException("Category", categoryId);

        var declared = category.Fields.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        var missing = provider.RequiredFields.Where(f => !declared.Contains(f.Key)).ToArray();

        return (category, missing);
    }
}

public sealed class MissingProviderFieldsQueryHandler(
    ProviderRegistry registry,
    ICategoryRepository categories) : IRequestHandler<MissingProviderFieldsQuery, IReadOnlyList<CategoryFieldDto>>
{
    public async Task<IReadOnlyList<CategoryFieldDto>> Handle(
        MissingProviderFieldsQuery request, CancellationToken cancellationToken)
    {
        var (_, missing) = await ProviderFields.ResolveAsync(
            registry, categories, request.CategoryId, request.Provider, cancellationToken);

        return missing.Select(CategoryMapper.ToDto).ToArray();
    }
}

public sealed class EnsureProviderFieldsCommandHandler(
    ProviderRegistry registry,
    ICategoryRepository categories) : IRequestHandler<EnsureProviderFieldsCommand, CategoryDto>
{
    public async Task<CategoryDto> Handle(
        EnsureProviderFieldsCommand request, CancellationToken cancellationToken)
    {
        var (category, missing) = await ProviderFields.ResolveAsync(
            registry, categories, request.CategoryId, request.Provider, cancellationToken);

        if (missing.Count > 0)
        {
            // 只追加缺的。已存在的 key 原封不動——使用者可能改過 Label。
            // 複製新實例，避免把 provider 持有的定義物件交給資料庫層。
            category.Fields.AddRange(missing.Select(f => new CategoryField
            {
                Key = f.Key,
                Label = f.Label,
                Type = f.Type,
                Options = f.Options?.ToList(),
                Required = f.Required,
                Searchable = f.Searchable,
                ShowOnCard = f.ShowOnCard
            }));

            // 系統品類在這裡被 ForbiddenException 擋下，這是正確行為
            await categories.UpdateAsync(category, cancellationToken);
        }

        return CategoryMapper.ToDto(category);
    }
}
