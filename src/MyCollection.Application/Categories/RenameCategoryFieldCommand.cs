using FluentValidation;
using FluentValidation.Results;
using MediatR;
using MongoDB.Bson;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Categories;

public record RenameCategoryFieldCommand(string CategoryId, string Key, string NewKey) : IRequest<RenameFieldResultDto>;

/// <summary>MovedItemCount 是使用者唯一能確認「真的動到資料」的證據，一定要回。</summary>
public record RenameFieldResultDto(CategoryDto Category, long MovedItemCount);

public sealed class RenameCategoryFieldCommandValidator : AbstractValidator<RenameCategoryFieldCommand>
{
    public RenameCategoryFieldCommandValidator()
    {
        RuleFor(x => x.CategoryId).Must(id => ObjectId.TryParse(id, out _)).WithMessage("Invalid category id.");
        RuleFor(x => x.Key).NotEmpty();
        RuleFor(x => x.NewKey)
            .NotEmpty()
            .Must(k => CategoryRules.FieldKeyPattern.IsMatch(k))
            .WithMessage("Field key must be camelCase (letters and digits, starting with a lowercase letter).")
            .NotEqual(x => x.Key).WithMessage("New key must differ from the current key.");
    }
}

public sealed class RenameCategoryFieldCommandHandler(
    ICategoryRepository categories,
    ICategoryFieldRenamer renamer,
    IProtectedFieldKeys protectedKeys,
    TimeProvider timeProvider)
    : IRequestHandler<RenameCategoryFieldCommand, RenameFieldResultDto>
{
    public async Task<RenameFieldResultDto> Handle(RenameCategoryFieldCommand request, CancellationToken cancellationToken)
    {
        var id = ObjectId.Parse(request.CategoryId);
        var category = await categories.GetAsync(id, cancellationToken)
                       ?? throw new NotFoundException(nameof(Category), request.CategoryId);

        if (category.OwnerId is null)
        {
            throw new ForbiddenException("System categories cannot be modified.");
        }

        var field = category.Fields.FirstOrDefault(f => string.Equals(f.Key, request.Key, StringComparison.Ordinal))
                    ?? throw new NotFoundException(nameof(CategoryField), request.Key);

        if (protectedKeys.IsProtected(request.Key))
        {
            throw new ValidationException([new ValidationFailure("Key", $"'{request.Key}' is a provider field and cannot be renamed.")]);
        }

        if (category.Fields.Any(f => string.Equals(f.Key, request.NewKey, StringComparison.Ordinal)))
        {
            throw new ValidationException([new ValidationFailure("NewKey", $"'{request.NewKey}' is already declared.")]);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var moved = await renamer.RenameAsync(id, request.Key, request.NewKey, now, cancellationToken);

        // renamer 成功後才改 in-memory，供回傳 DTO；擲例外時物件保持原狀
        field.Key = request.NewKey;
        category.UpdatedAt = now;

        return new RenameFieldResultDto(CategoryMapper.ToDto(category), moved);
    }
}
