using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Categories;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class ItemCommandTests
{
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero));

    private static readonly ObjectId CategoryId = ObjectId.GenerateNewId();

    private static readonly Category FigureCategory = new()
    {
        Id = CategoryId,
        Name = "公仔",
        Kind = CategoryKind.Physical,
        Fields =
        [
            new CategoryField { Key = "brand", Label = "廠商", Type = FieldType.Text, Required = true },
            new CategoryField { Key = "scale", Label = "比例", Type = FieldType.Text }
        ]
    };

    public ItemCommandTests()
    {
        _categories.Setup(r => r.GetAsync(CategoryId, It.IsAny<CancellationToken>())).ReturnsAsync(FigureCategory);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private CreateItemCommandHandler CreateSut() =>
        new(_items.Object, _categories.Object, new AttributeValidator(), _time);

    private static CreateItemCommand Command(string attributes = """{ "brand": "GSC" }""") => new(
        CategoryId.ToString(),
        "初音ミク 1/8",
        "描述",
        ["GSC"],
        false,
        Json(attributes),
        null);

    [Fact]
    public async Task Creates_item_with_timestamps_and_manual_source()
    {
        Item? saved = null;
        _items.Setup(r => r.InsertAsync(It.IsAny<Item>(), It.IsAny<CancellationToken>()))
            .Callback<Item, CancellationToken>((i, _) => saved = i)
            .Returns(Task.CompletedTask);

        var dto = await CreateSut().Handle(Command(), CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.Source.Should().Be(ItemSource.Manual);
        saved.CategoryId.Should().Be(CategoryId);
        saved.Attributes["brand"].AsString.Should().Be("GSC");
        saved.CreatedAt.Should().Be(new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc));
        saved.UpdatedAt.Should().Be(saved.CreatedAt);

        dto.Attributes["brand"].Should().Be("GSC");
        dto.Tags.Should().BeEquivalentTo("GSC");
    }

    [Fact]
    public async Task Throws_NotFound_for_unknown_category()
    {
        _categories.Setup(r => r.GetAsync(It.IsAny<ObjectId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        var act = () => CreateSut().Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Throws_ValidationException_when_attributes_violate_schema()
    {
        var act = () => CreateSut().Handle(Command("""{ "scale": "1/8" }"""), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be("attributes.brand");
    }

    [Fact]
    public async Task Forces_null_location_for_digital_category()
    {
        var digital = new Category
        {
            Id = CategoryId, Name = "數位遊戲", Kind = CategoryKind.Digital, Fields = []
        };
        _categories.Setup(r => r.GetAsync(CategoryId, It.IsAny<CancellationToken>())).ReturnsAsync(digital);

        Item? saved = null;
        _items.Setup(r => r.InsertAsync(It.IsAny<Item>(), It.IsAny<CancellationToken>()))
            .Callback<Item, CancellationToken>((i, _) => saved = i)
            .Returns(Task.CompletedTask);

        var command = Command("{}") with { LocationId = ObjectId.GenerateNewId().ToString() };
        await CreateSut().Handle(command, CancellationToken.None);

        saved!.LocationId.Should().BeNull("digital 品類的 locationId 恆為 null");
    }

    [Fact]
    public async Task Update_preserves_immutable_fields()
    {
        var existing = new Item
        {
            Id = ObjectId.GenerateNewId(),
            OwnerId = ObjectId.GenerateNewId(),
            CategoryId = CategoryId,
            Name = "舊名稱",
            Source = ItemSource.Steam,
            ExternalRef = new ExternalRef { Provider = "steam", ExternalId = "440" },
            Images = [new ItemImage { Id = "img1", Path = "p", CardPath = "c", ThumbPath = "t", IsPrimary = true }],
            Attributes = new BsonDocument("brand", "Valve"),
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        _items.Setup(r => r.GetAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        Item? saved = null;
        _items.Setup(r => r.UpdateAsync(It.IsAny<Item>(), It.IsAny<CancellationToken>()))
            .Callback<Item, CancellationToken>((i, _) => saved = i)
            .Returns(Task.CompletedTask);

        var command = new UpdateItemCommand(
            existing.Id.ToString(), CategoryId.ToString(), "新名稱", null, ["FPS"], true,
            Json("""{ "brand": "Valve" }"""), null);

        await new UpdateItemCommandHandler(_items.Object, _categories.Object, new AttributeValidator(), _time)
            .Handle(command, CancellationToken.None);

        saved!.Name.Should().Be("新名稱");
        saved.IsShowcased.Should().BeTrue();
        saved.Source.Should().Be(ItemSource.Steam, "來源不可由使用者改寫");
        saved.ExternalRef!.ExternalId.Should().Be("440", "外部參照不可由使用者改寫");
        saved.Images.Should().ContainSingle("圖片由 Media 模組管理，不透過品項更新");
        saved.CreatedAt.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        saved.UpdatedAt.Should().Be(new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Search_validator_rejects_out_of_range_page_size()
    {
        var validator = new SearchItemsQueryValidator();

        validator.Validate(new SearchItemsQuery(PageSize: 0)).IsValid.Should().BeFalse();
        validator.Validate(new SearchItemsQuery(PageSize: 500)).IsValid.Should().BeFalse();
        validator.Validate(new SearchItemsQuery(Page: 0)).IsValid.Should().BeFalse();
        validator.Validate(new SearchItemsQuery()).IsValid.Should().BeTrue();
    }
}
