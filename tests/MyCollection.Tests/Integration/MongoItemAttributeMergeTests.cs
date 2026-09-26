using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

/// <summary>
/// tech debt C1：編輯品項不可刪除未宣告屬性（ADR-0012 §三）。
/// 用真的 Mongo 驗證，因為缺陷就在「寫入語法把整份 attributes 換掉」這一層。
/// </summary>
[Collection(MongoCollection.Name)]
public class MongoItemAttributeMergeTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();

    private static readonly Category FigureCategory = new()
    {
        Id = ObjectId.GenerateNewId(),
        Name = "公仔",
        Kind = CategoryKind.Physical,
        Fields =
        [
            new CategoryField { Key = "brand", Label = "廠商", Type = FieldType.Text },
            new CategoryField { Key = "scale", Label = "比例", Type = FieldType.Text }
        ]
    };

    private static readonly Category ModelCategory = new()
    {
        Id = ObjectId.GenerateNewId(),
        Name = "模型",
        Kind = CategoryKind.Physical,
        Fields =
        [
            new CategoryField { Key = "brand", Label = "廠商", Type = FieldType.Text },
            new CategoryField { Key = "grade", Label = "等級", Type = FieldType.Text }
        ]
    };

    private readonly Mock<ICategoryRepository> _categories = new();
    private MongoItemRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        var userContext = new Mock<IUserContext>();
        userContext.SetupGet(c => c.UserId).Returns(Owner);
        _repository = new MongoItemRepository(fixture.Context, userContext.Object);

        _categories.Setup(r => r.GetAsync(FigureCategory.Id, It.IsAny<CancellationToken>())).ReturnsAsync(FigureCategory);
        _categories.Setup(r => r.GetAsync(ModelCategory.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ModelCategory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>brand、scale 已宣告；legacyNote 是撤回宣告後留下的未宣告屬性。</summary>
    private async Task<Item> SeedAsync()
    {
        var item = new Item
        {
            Id = ObjectId.GenerateNewId(),
            OwnerId = Owner,
            CategoryId = FigureCategory.Id,
            Name = "初音ミク 1/8",
            Source = ItemSource.Manual,
            Attributes = new BsonDocument
            {
                { "brand", "GSC" },
                { "scale", "1/8" },
                { "legacyNote", "限定版" }
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await fixture.Context.Items.InsertOneAsync(item);
        return item;
    }

    private async Task<BsonDocument> StoredAttributesAsync(ObjectId id) =>
        (await fixture.Context.Items.Find(Builders<Item>.Filter.Eq(x => x.Id, id)).FirstAsync()).Attributes;

    private Task<ItemDto> EditAsync(Item item, ObjectId categoryId, string attributes) =>
        new UpdateItemCommandHandler(
                _repository,
                _categories.Object,
                new AttributeValidator(),
                new FakeTimeProvider(DateTimeOffset.UtcNow),
                Mock.Of<Application.Showcase.IShowcaseImageQueue>())
            .Handle(
                new UpdateItemCommand(
                    item.Id.ToString(), categoryId.ToString(), item.Name, null, [], false,
                    JsonDocument.Parse(attributes).RootElement.Clone(), null),
                CancellationToken.None);

    [Fact]
    public async Task Editing_an_item_keeps_its_undeclared_attributes()
    {
        var item = await SeedAsync();

        var dto = await EditAsync(item, FigureCategory.Id, """{ "brand": "Max Factory", "scale": "1/7" }""");

        var stored = await StoredAttributesAsync(item.Id);
        stored["brand"].AsString.Should().Be("Max Factory");
        stored["scale"].AsString.Should().Be("1/7");
        stored["legacyNote"].AsString.Should().Be("限定版", "撤回宣告不刪值，重新宣告時值要能回來");
        dto.Attributes.Should().ContainKey("legacyNote", "回傳的 DTO 要與資料庫寫入後的狀態一致");
    }

    [Fact]
    public async Task Declared_keys_that_are_null_or_absent_are_removed()
    {
        var item = await SeedAsync();

        await EditAsync(item, FigureCategory.Id, """{ "brand": null }""");

        var stored = await StoredAttributesAsync(item.Id);
        stored.Contains("brand").Should().BeFalse("值為 null 代表清空");
        stored.Contains("scale").Should().BeFalse("請求沒出現的已宣告鍵也代表清空");
        stored["legacyNote"].AsString.Should().Be("限定版");
    }

    [Fact]
    public async Task Changing_category_keeps_keys_the_new_category_does_not_declare()
    {
        var item = await SeedAsync();

        await EditAsync(item, ModelCategory.Id, """{ "grade": "MG" }""");

        var stored = await StoredAttributesAsync(item.Id);
        stored["grade"].AsString.Should().Be("MG");
        stored.Contains("brand").Should().BeFalse("brand 在新品類有宣告而請求沒帶，屬於清空");
        stored["scale"].AsString.Should().Be("1/8", "scale 對新品類是未宣告屬性，不可刪");
        stored["legacyNote"].AsString.Should().Be("限定版");
    }

    [Fact]
    public async Task Update_without_attribute_changes_never_touches_attributes()
    {
        var item = await SeedAsync();

        // 圖片 handler 的路徑：先讀出整筆，期間 attributes 已被其他寫入改變，再整筆寫回。
        await fixture.Context.Items.UpdateOneAsync(
            Builders<Item>.Filter.Eq(x => x.Id, item.Id),
            Builders<Item>.Update.Set("attributes.brand", "Good Smile"));
        item.Attributes = new BsonDocument();
        item.Name = "改過的名稱";

        await _repository.UpdateAsync(item, CancellationToken.None);

        var stored = await StoredAttributesAsync(item.Id);
        stored["brand"].AsString.Should().Be("Good Smile");
        stored["scale"].AsString.Should().Be("1/8");
        stored["legacyNote"].AsString.Should().Be("限定版");
    }
}
