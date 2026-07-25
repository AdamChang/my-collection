using MyCollection.Domain.Entities;

namespace MyCollection.Application.Categories;

public record CategoryFieldDto(
    string Key,
    string Label,
    string Type,
    IReadOnlyList<string>? Options,
    bool Required,
    bool Searchable,
    bool ShowOnCard);

public record CategoryDto(
    string Id,
    string Name,
    string Icon,
    string Kind,
    bool IsSystem,
    IReadOnlyList<CategoryFieldDto> Fields);

public static class CategoryMapper
{
    public static CategoryDto ToDto(Category category) => new(
        category.Id.ToString(),
        category.Name,
        category.Icon,
        category.Kind.ToString(),
        category.OwnerId is null,
        category.Fields.Select(ToDto).ToArray());

    public static CategoryFieldDto ToDto(CategoryField field) => new(
        field.Key,
        field.Label,
        field.Type.ToString(),
        field.Options,
        field.Required,
        field.Searchable,
        field.ShowOnCard);

    public static CategoryField ToEntity(CategoryFieldDto dto) => new()
    {
        Key = dto.Key,
        Label = dto.Label,
        Type = Enum.Parse<FieldType>(dto.Type, ignoreCase: true),
        Options = dto.Options?.ToList(),
        Required = dto.Required,
        Searchable = dto.Searchable,
        ShowOnCard = dto.ShowOnCard
    };
}
