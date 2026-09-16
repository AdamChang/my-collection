using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Categories;

public record CreateCategoryCommand(
    string Name,
    string Icon,
    string Kind,
    string DefaultDisplayMode,
    IReadOnlyList<CategoryFieldDto> Fields) : IRequest<CategoryDto>;

public record UpdateCategoryCommand(
    string Id,
    string Name,
    string Icon,
    string Kind,
    string DefaultDisplayMode,
    IReadOnlyList<CategoryFieldDto> Fields) : IRequest<CategoryDto>;

public record DeleteCategoryCommand(string Id) : IRequest;

/// <summary>Create/Update 共用的欄位規則。</summary>
public static partial class CategoryRules
{
    [GeneratedRegex("^[a-z][a-zA-Z0-9]*$")]
    public static partial Regex FieldKeyPattern { get; }

    public static void ApplyTo<T>(
        AbstractValidator<T> validator,
        Func<T, string> kind,
        Func<T, string> defaultDisplayMode,
        Func<T, IReadOnlyList<CategoryFieldDto>> fields)
    {
        validator.RuleFor(x => kind(x))
            .Must(k => Enum.TryParse<CategoryKind>(k, ignoreCase: true, out _))
            .WithName("Kind")
            .WithMessage("Kind must be 'Physical' or 'Digital'.");

        validator.RuleFor(x => defaultDisplayMode(x))
            .Must(m => Enum.TryParse<DisplayMode>(m, ignoreCase: true, out _))
            .WithName("DefaultDisplayMode")
            .WithMessage("DefaultDisplayMode must be 'List', 'Hero' or 'Stats'.");

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
        CategoryRules.ApplyTo(this, x => x.Kind, x => x.DefaultDisplayMode, x => x.Fields);
    }
}

public sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(x => x.Id).Must(id => ObjectId.TryParse(id, out _)).WithMessage("Invalid category id.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Icon).NotEmpty().MaximumLength(32);
        CategoryRules.ApplyTo(this, x => x.Kind, x => x.DefaultDisplayMode, x => x.Fields);
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
            DefaultDisplayMode = Enum.Parse<DisplayMode>(request.DefaultDisplayMode, ignoreCase: true),
            Fields = request.Fields.Select(CategoryMapper.ToEntity).ToList(),
            CreatedAt = now,
            UpdatedAt = now
        };

        await repository.InsertAsync(category, cancellationToken);

        return CategoryMapper.ToDto(category);
    }
}

public sealed class UpdateCategoryCommandHandler(
    ICategoryRepository repository,
    TimeProvider timeProvider,
    IProtectedFieldKeys protectedKeys)
    : IRequestHandler<UpdateCategoryCommand, CategoryDto>
{
    public async Task<CategoryDto> Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        var id = ObjectId.Parse(request.Id);
        var existing = await repository.GetAsync(id, cancellationToken)
                       ?? throw new NotFoundException(nameof(Category), request.Id);

        // PUT 的語意是宣告集合的置換：請求裡沒有的既有鍵就是撤回宣告（ADR-0012 §二）。
        // 撤回不刪品項上的值，但受保護的鍵連撤回都不行——來源是用它找到欄位的（§四）。
        var requested = request.Fields.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        var withdrawnProtected = existing.Fields
            .Select(f => f.Key)
            .Where(k => !requested.Contains(k) && protectedKeys.IsProtected(k))
            .ToArray();

        if (withdrawnProtected.Length > 0)
        {
            throw new ValidationException(withdrawnProtected.Select(k =>
                new ValidationFailure("Fields", $"'{k}' is a provider field and cannot be removed.")));
        }

        existing.Name = request.Name.Trim();
        existing.Icon = request.Icon;
        existing.Kind = Enum.Parse<CategoryKind>(request.Kind, ignoreCase: true);
        existing.DefaultDisplayMode = Enum.Parse<DisplayMode>(request.DefaultDisplayMode, ignoreCase: true);
        existing.Fields = request.Fields.Select(CategoryMapper.ToEntity).ToList();
        existing.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await repository.UpdateAsync(existing, cancellationToken);

        return CategoryMapper.ToDto(existing);
    }
}

public sealed class DeleteCategoryCommandHandler(ICategoryRepository repository, IItemRepository items)
    : IRequestHandler<DeleteCategoryCommand>
{
    public async Task Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(request.Id, out var id))
        {
            throw new NotFoundException(nameof(Category), request.Id);
        }

        // 順序：先確認存在與擁有權，再計數，最後刪。先計數的話，使用者在系統品類下
        // 有品項時會拿到 409 而不是 403。
        var existing = await repository.GetAsync(id, cancellationToken)
                       ?? throw new NotFoundException(nameof(Category), request.Id);

        if (existing.OwnerId is null)
        {
            throw new ForbiddenException("System categories cannot be deleted.");
        }

        // 品項不會失去品類、也不會被連帶刪掉（ADR-0012 §五）
        var count = await items.CountByCategoryAsync(id, cancellationToken);
        if (count > 0)
        {
            throw new ConflictException($"Category still has {count} item(s); move or delete them first.");
        }

        await repository.DeleteAsync(id, cancellationToken);
    }
}
