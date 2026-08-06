using FluentAssertions;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Application.Showcase;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class ShowcaseQueryTests
{
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<ICategoryRepository> _categories = new();

    public ShowcaseQueryTests()
    {
        _categories.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    private GetShowcaseQueryHandler Sut() => new(_items.Object, _categories.Object);

    [Fact]
    public async Task Always_filters_to_showcased_items_regardless_of_caller_input()
    {
        ItemQuerySpec? captured = null;
        _items.Setup(r => r.SearchAsync(It.IsAny<ItemQuerySpec>(), It.IsAny<CancellationToken>()))
            .Callback<ItemQuerySpec, CancellationToken>((s, _) => captured = s)
            .ReturnsAsync(new PagedResult<Item>([], 0, 1, 24));

        await Sut().Handle(new GetShowcaseQuery(2, 12), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.IsShowcased.Should().BeTrue();
        captured.CategoryId.Should().BeNull("Showcase 牆跨品類混合顯示");
        captured.Page.Should().Be(2);
        captured.PageSize.Should().Be(12);
    }

    [Fact]
    public async Task Maps_items_to_dtos()
    {
        var item = new Item
        {
            Id = ObjectId.GenerateNewId(),
            OwnerId = ObjectId.GenerateNewId(),
            CategoryId = ObjectId.GenerateNewId(),
            Name = "Portal 2",
            IsShowcased = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _items.Setup(r => r.SearchAsync(It.IsAny<ItemQuerySpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<Item>([item], 1, 1, 24));

        var result = await Sut().Handle(new GetShowcaseQuery(), CancellationToken.None);

        result.Total.Should().Be(1);
        result.Items.Should().ContainSingle().Which.Name.Should().Be("Portal 2");
    }

    [Fact]
    public async Task Item_without_override_inherits_the_category_default_display_mode()
    {
        var categoryId = ObjectId.GenerateNewId();
        var item = new Item
        {
            Id = ObjectId.GenerateNewId(),
            OwnerId = ObjectId.GenerateNewId(),
            CategoryId = categoryId,
            Name = "公仔",
            IsShowcased = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _items.Setup(r => r.SearchAsync(It.IsAny<ItemQuerySpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<Item>([item], 1, 1, 24));
        _categories.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new Category
            {
                Id = categoryId, Name = "公仔模型", DefaultDisplayMode = DisplayMode.Hero,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            }
        ]);

        var result = await Sut().Handle(new GetShowcaseQuery(), CancellationToken.None);

        result.Items.Should().ContainSingle().Which.EffectiveDisplayMode.Should().Be("Hero");
    }

    [Fact]
    public async Task Item_level_override_wins_over_the_category_default()
    {
        var categoryId = ObjectId.GenerateNewId();
        var item = new Item
        {
            Id = ObjectId.GenerateNewId(),
            OwnerId = ObjectId.GenerateNewId(),
            CategoryId = categoryId,
            Name = "公仔",
            IsShowcased = true,
            DisplayMode = DisplayMode.List,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _items.Setup(r => r.SearchAsync(It.IsAny<ItemQuerySpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<Item>([item], 1, 1, 24));
        _categories.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new Category
            {
                Id = categoryId, Name = "公仔模型", DefaultDisplayMode = DisplayMode.Hero,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            }
        ]);

        var result = await Sut().Handle(new GetShowcaseQuery(), CancellationToken.None);

        result.Items.Should().ContainSingle().Which.EffectiveDisplayMode.Should().Be("List");
    }

    [Fact]
    public void Validator_bounds_paging()
    {
        var validator = new GetShowcaseQueryValidator();

        validator.Validate(new GetShowcaseQuery(0, 24)).IsValid.Should().BeFalse();
        validator.Validate(new GetShowcaseQuery(1, 0)).IsValid.Should().BeFalse();
        validator.Validate(new GetShowcaseQuery(1, 201)).IsValid.Should().BeFalse();
        validator.Validate(new GetShowcaseQuery()).IsValid.Should().BeTrue();
    }
}
