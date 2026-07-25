using System.Text.RegularExpressions;
using FluentValidation;
using MediatR;
using MongoDB.Bson;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Categories;

public record CreateCategoryCommand(
    string Name,
    string Icon,
    string Kind,
    IReadOnlyList<CategoryFieldDto> Fields) : IRequest<CategoryDto>;

public record UpdateCategoryCommand(
    string Id,
    string Name,
    string Icon,
    string Kind,
    IReadOnlyList<CategoryFieldDto> Fields) : IRequest<CategoryDto>;

public record DeleteCategoryCommand(string Id) : IRequest;

/// <summary>Create/Update 共用的欄位規則。</summary>
public static partial class CategoryRules
{
    [GeneratedRegex("^[a-z][a-zA-Z0-9]*$")]
    public static partial Regex FieldKeyPattern { get; }

    public static void ApplyTo<T>(AbstractValidator<T> validator, Func<T, string> kind, Func<T, IReadOnlyList<CategoryFieldDto>> fields)
    {
        validator.RuleFor(x => kind(x))
            .Must(k => Enum.TryParse<CategoryKind>(k, ignoreCase: true, out _))
            .WithName("Kind")
            .WithMessage("Kind must be 'Physical' or 'Digital'.");

        validator.RuleFor(x => fields(x))
            .NotNull()
            .WithName("Fields")
            .Must(f => f.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() == f.Count)
            .WithMessage("Field keys contain duplicate entries.");

        validator.RuleForEach(x => fields(x)).ChildRules(field =>
        {
            field.RuleFor(f => f.Key)
                .NotEmpty()
                .Must(k => FieldKeyPattern.IsMatch(k))
                .WithMessage("Field key must be camelCase (letters and digits, starting with a lowercase letter).");

            field.RuleFor(f => f.Label).NotEmpty().MaximumLength(64);

            field.RuleFor(f => f.Type)
                .Must(t => Enum.TryParse<FieldType>(t, ignoreCase: true, out _))
                .WithMessage("Unknown field type.");

            field.RuleFor(f => f.Options)
                .NotNull().NotEmpty()
                .When(f => string.Equals(f.Type, nameof(FieldType.Select), StringComparison.OrdinalIgnoreCase))
                .WithMessage("A Select field requires at least one option.");
        }).WithName("Fields");
    }
}

public sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Icon).NotEmpty().MaximumLength(32);
        CategoryRules.ApplyTo(this, x => x.Kind, x => x.Fields);
    }
}

public sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(x => x.Id).Must(id => ObjectId.TryParse(id, out _)).WithMessage("Invalid category id.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Icon).NotEmpty().MaximumLength(32);
        CategoryRules.ApplyTo(this, x => x.Kind, x => x.Fields);
    }
}

public sealed class CreateCategoryCommandHandler(ICategoryRepository repository, TimeProvider timeProvider)
    : IRequestHandler<CreateCategoryCommand, CategoryDto>
{
    public async Task<CategoryDto> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var category = new Category
        {
            Id = ObjectId.GenerateNewId(),
            Name = request.Name.Trim(),
            Icon = request.Icon,
            Kind = Enum.Parse<CategoryKind>(request.Kind, ignoreCase: true),
            Fields = request.Fields.Select(CategoryMapper.ToEntity).ToList(),
            CreatedAt = now,
            UpdatedAt = now
        };

        await repository.InsertAsync(category, cancellationToken);

        return CategoryMapper.ToDto(category);
    }
}

public sealed class UpdateCategoryCommandHandler(ICategoryRepository repository, TimeProvider timeProvider)
    : IRequestHandler<UpdateCategoryCommand, CategoryDto>
{
    public async Task<CategoryDto> Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        var id = ObjectId.Parse(request.Id);
        var existing = await repository.GetAsync(id, cancellationToken)
                       ?? throw new NotFoundException(nameof(Category), request.Id);

        existing.Name = request.Name.Trim();
        existing.Icon = request.Icon;
        existing.Kind = Enum.Parse<CategoryKind>(request.Kind, ignoreCase: true);
        existing.Fields = request.Fields.Select(CategoryMapper.ToEntity).ToList();
        existing.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await repository.UpdateAsync(existing, cancellationToken);

        return CategoryMapper.ToDto(existing);
    }
}

public sealed class DeleteCategoryCommandHandler(ICategoryRepository repository)
    : IRequestHandler<DeleteCategoryCommand>
{
    public Task Handle(DeleteCategoryCommand request, CancellationToken cancellationToken) =>
        repository.DeleteAsync(ObjectId.Parse(request.Id), cancellationToken);
}
