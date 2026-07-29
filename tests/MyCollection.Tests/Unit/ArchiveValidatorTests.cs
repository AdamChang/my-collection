using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Application.Items;
using MyCollection.Application.Transfer;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class ArchiveValidatorTests
{
    private static readonly ObjectId SystemCategoryId = ObjectId.Parse("000000000000000000000002");

    private readonly ArchiveValidator _sut = new(new AttributeValidator());

    private static ArchiveCategory Category(ObjectId id, string name = "黑膠唱片") => new()
    {
        Id = id,
        Name = name,
        Fields = [new ArchiveCategoryField { Key = "label", Label = "廠牌", Type = FieldType.Text }],
        CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static ArchiveItem Item(ObjectId categoryId, BsonDocument? attributes = null) => new()
    {
        Id = ObjectId.GenerateNewId(),
        CategoryId = categoryId,
        Name = "Kind of Blue",
        Attributes = attributes ?? [],
        CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static Category SystemCategory() => new()
    {
        Id = SystemCategoryId,
        OwnerId = null,
        Name = "數位遊戲",
        Kind = CategoryKind.Digital,
        Fields = [],
        CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void Valid_manifest_produces_no_failures()
    {
        var categoryId = ObjectId.GenerateNewId();
        var manifest = new ArchiveManifest
        {
            Categories = [Category(categoryId)],
            Items = [Item(categoryId, new BsonDocument { { "label", "Columbia" } })]
        };

        _sut.Validate(manifest, [SystemCategory()]).Should().BeEmpty();
    }

    [Fact]
    public void Item_pointing_at_a_system_category_is_accepted()
    {
        var manifest = new ArchiveManifest { Items = [Item(SystemCategoryId)] };

        _sut.Validate(manifest, [SystemCategory()]).Should().BeEmpty();
    }

    [Fact]
    public void Item_pointing_at_an_unknown_category_is_rejected()
    {
        var manifest = new ArchiveManifest { Items = [Item(ObjectId.GenerateNewId())] };

        _sut.Validate(manifest, [SystemCategory()])
            .Should().ContainSingle().Which.ErrorMessage.Should().Contain("category");
    }

    [Fact]
    public void Attributes_that_break_the_category_schema_are_rejected()
    {
        var categoryId = ObjectId.GenerateNewId();
        var manifest = new ArchiveManifest
        {
            Categories = [Category(categoryId)],
            Items = [Item(categoryId, new BsonDocument { { "label", 42 } })]
        };

        _sut.Validate(manifest, [SystemCategory()])
            .Should().ContainSingle().Which.PropertyName.Should().Contain("label");
    }

    [Fact]
    public void Blank_names_are_rejected_for_both_categories_and_items()
    {
        var categoryId = ObjectId.GenerateNewId();
        var manifest = new ArchiveManifest
        {
            Categories = [Category(categoryId, name: "  ")],
            Items = [new ArchiveItem
            {
                Id = ObjectId.GenerateNewId(),
                CategoryId = categoryId,
                Name = "",
                CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
            }]
        };

        _sut.Validate(manifest, [SystemCategory()]).Should().HaveCount(2);
    }

    [Fact]
    public void All_failures_are_reported_not_just_the_first()
    {
        var manifest = new ArchiveManifest
        {
            Items = [Item(ObjectId.GenerateNewId()), Item(ObjectId.GenerateNewId())]
        };

        _sut.Validate(manifest, [SystemCategory()]).Should().HaveCount(2);
    }
}
