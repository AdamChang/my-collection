using System.Text.Json;
using FluentValidation;
using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Items;

public record AcquisitionInput(DateTime? AcquiredAt, decimal? Amount, string? Currency, string? Vendor);

public record CreateItemCommand(
    string CategoryId,
    string Name,
    string? Description,
    IReadOnlyList<string> Tags,
    bool IsShowcased,
    JsonElement Attributes,
    AcquisitionInput? Acquisition,
    string? LocationId = null) : IRequest<ItemDto>;

public record UpdateItemCommand(
    string Id,
    string CategoryId,
    string Name,
    string? Description,
    IReadOnlyList<string> Tags,
    bool IsShowcased,
    JsonElement Attributes,
    AcquisitionInput? Acquisition,
    string? LocationId = null) : IRequest<ItemDto>;

public record DeleteItemCommand(string Id) : IRequest;

public sealed class CreateItemCommandValidator : AbstractValidator<CreateItemCommand>
{
    public CreateItemCommandValidator()
    {
        RuleFor(x => x.CategoryId).Must(id => ObjectId.TryParse(id, out _)).WithMessage("Invalid category id.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleForEach(x => x.Tags).NotEmpty().MaximumLength(50);
    }
}

public sealed class UpdateItemCommandValidator : AbstractValidator<UpdateItemCommand>
{
    public UpdateItemCommandValidator()
    {
        RuleFor(x => x.Id).Must(id => ObjectId.TryParse(id, out _)).WithMessage("Invalid item id.");
        RuleFor(x => x.CategoryId).Must(id => ObjectId.TryParse(id, out _)).WithMessage("Invalid category id.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleForEach(x => x.Tags).NotEmpty().MaximumLength(50);
    }
}

/// <summary>Create 與 Update 共用的 schema 驗證與欄位套用邏輯。</summary>
internal static class ItemWriteHelper
{
    public static async Task<(Category Category, BsonDocument Attributes)> ResolveAsync(
        ICategoryRepository categories,
        IAttributeValidator attributeValidator,
        string categoryId,
        JsonElement attributes,
        CancellationToken ct)
    {
        var id = ObjectId.Parse(categoryId);
        var category = await categories.GetAsync(id, ct)
                       ?? throw new NotFoundException(nameof(Category), categoryId);

        var document = attributes.ValueKind == JsonValueKind.Undefined
            ? new BsonDocument()
            : BsonJson.ToBson(attributes);

        var failures = attributeValidator.Validate(category, document);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return (category, document);
    }

    public static Acquisition? ToAcquisition(AcquisitionInput? input)
    {
        if (input is null)
        {
            return null;
        }

        return new Acquisition
        {
            // 沒帶 Z 的輸入視為 UTC；資料層的 UtcOnlyDateTimeSerializer 會拒絕非 UTC 值
            AcquiredAt = UtcDate.Normalise(input.AcquiredAt),
            Price = input.Amount is { } amount
                ? new Money(amount, string.IsNullOrWhiteSpace(input.Currency) ? "TWD" : input.Currency)
                : null,
            Vendor = input.Vendor
        };
    }

    public static ObjectId? ToLocationId(Category category, string? locationId) =>
        category.Kind == CategoryKind.Digital || string.IsNullOrWhiteSpace(locationId)
            ? null
            : ObjectId.Parse(locationId);

    public static List<string> NormaliseTags(IReadOnlyList<string> tags) =>
        tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
}

public sealed class CreateItemCommandHandler(
    IItemRepository items,
    ICategoryRepository categories,
    IAttributeValidator attributeValidator,
    TimeProvider timeProvider) : IRequestHandler<CreateItemCommand, ItemDto>
{
    public async Task<ItemDto> Handle(CreateItemCommand request, CancellationToken cancellationToken)
    {
        var (category, attributes) = await ItemWriteHelper.ResolveAsync(
            categories, attributeValidator, request.CategoryId, request.Attributes, cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var item = new Item
        {
            Id = ObjectId.GenerateNewId(),
            CategoryId = category.Id,
            Name = request.Name.Trim(),
            Description = request.Description,
            Tags = ItemWriteHelper.NormaliseTags(request.Tags),
            IsShowcased = request.IsShowcased,
            Source = ItemSource.Manual,
            Acquisition = ItemWriteHelper.ToAcquisition(request.Acquisition),
            LocationId = ItemWriteHelper.ToLocationId(category, request.LocationId),
            Attributes = attributes,
            CreatedAt = now,
            UpdatedAt = now
        };

        await items.InsertAsync(item, cancellationToken);

        return ItemMapper.ToDto(item);
    }
}

public sealed class UpdateItemCommandHandler(
    IItemRepository items,
    ICategoryRepository categories,
    IAttributeValidator attributeValidator,
    TimeProvider timeProvider) : IRequestHandler<UpdateItemCommand, ItemDto>
{
    public async Task<ItemDto> Handle(UpdateItemCommand request, CancellationToken cancellationToken)
    {
        var id = ObjectId.Parse(request.Id);
        var existing = await items.GetAsync(id, cancellationToken)
                       ?? throw new NotFoundException(nameof(Item), request.Id);

        var (category, attributes) = await ItemWriteHelper.ResolveAsync(
            categories, attributeValidator, request.CategoryId, request.Attributes, cancellationToken);

        // Source / ExternalRef / Images / CreatedAt 不接受使用者輸入，由同步與 Media 模組管理
        existing.CategoryId = category.Id;
        existing.Name = request.Name.Trim();
        existing.Description = request.Description;
        existing.Tags = ItemWriteHelper.NormaliseTags(request.Tags);
        existing.IsShowcased = request.IsShowcased;
        existing.Acquisition = ItemWriteHelper.ToAcquisition(request.Acquisition);
        existing.LocationId = ItemWriteHelper.ToLocationId(category, request.LocationId);
        existing.Attributes = attributes;
        existing.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await items.UpdateAsync(existing, cancellationToken);

        return ItemMapper.ToDto(existing);
    }
}

public sealed class DeleteItemCommandHandler(IItemRepository items) : IRequestHandler<DeleteItemCommand>
{
    public Task Handle(DeleteItemCommand request, CancellationToken cancellationToken) =>
        items.DeleteAsync(ObjectId.Parse(request.Id), cancellationToken);
}
