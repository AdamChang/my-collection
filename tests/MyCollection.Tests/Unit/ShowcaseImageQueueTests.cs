using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Categories;
using MyCollection.Application.Items;
using MyCollection.Application.Showcase;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Imaging;

namespace MyCollection.Tests.Unit;

public class ShowcaseImageQueueTests
{
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly Mock<IShowcaseImageQueue> _queue = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero));

    private static readonly ObjectId CategoryId = ObjectId.GenerateNewId();

    private Item _item = null!;

    public ShowcaseImageQueueTests()
    {
        _categories.Setup(r => r.GetAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Category
            {
                Id = CategoryId,
                Name = "數位遊戲",
                Kind = CategoryKind.Digital,
                // headerUrl 必須宣告在 schema 才能通過 AttributeValidator：UpdateItemCommand
                // 會把整包 attributes 拿去驗證，Steam 同步寫入的 headerUrl 也不例外。
                Fields = [new CategoryField { Key = "headerUrl", Label = "封面圖", Type = FieldType.Url }]
            });

        _items.Setup(r => r.UpdateAsync(It.IsAny<Item>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    private void SeedItem(bool isShowcased, bool hasImages = false)
    {
        _item = new Item
        {
            Id = ObjectId.GenerateNewId(),
            OwnerId = ObjectId.GenerateNewId(),
            CategoryId = CategoryId,
            Name = "Portal 2",
            IsShowcased = isShowcased,
            Source = ItemSource.Steam,
            Attributes = new BsonDocument("headerUrl", "https://cdn/620.jpg"),
            Images = hasImages
                ? [new ItemImage { Id = "i", Path = "p", CardPath = "c", ThumbPath = "t", IsPrimary = true }]
                : [],
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _items.Setup(r => r.GetAsync(_item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => _item);
    }

    private Task UpdateAsync(bool isShowcased) =>
        new UpdateItemCommandHandler(_items.Object, _categories.Object, new AttributeValidator(), _time, _queue.Object)
            .Handle(new UpdateItemCommand(
                _item.Id.ToString(), CategoryId.ToString(), _item.Name, null, [], isShowcased,
                JsonDocument.Parse("""{ "headerUrl": "https://cdn/620.jpg" }""").RootElement.Clone(), null),
                CancellationToken.None);

    [Fact]
    public async Task Enqueues_download_when_item_becomes_showcased()
    {
        SeedItem(isShowcased: false);

        await UpdateAsync(isShowcased: true);

        _queue.Verify(q => q.Enqueue(_item.Id), Times.Once);
    }

    [Fact]
    public async Task Does_not_enqueue_when_already_showcased()
    {
        SeedItem(isShowcased: true);

        await UpdateAsync(isShowcased: true);

        _queue.Verify(q => q.Enqueue(It.IsAny<ObjectId>()), Times.Never);
    }

    [Fact]
    public async Task Does_not_enqueue_when_unshowcasing()
    {
        SeedItem(isShowcased: true);

        await UpdateAsync(isShowcased: false);

        _queue.Verify(q => q.Enqueue(It.IsAny<ObjectId>()), Times.Never);
    }

    [Fact]
    public async Task Does_not_enqueue_when_item_already_has_local_images()
    {
        SeedItem(isShowcased: false, hasImages: true);

        await UpdateAsync(isShowcased: true);

        _queue.Verify(q => q.Enqueue(It.IsAny<ObjectId>()), Times.Never);
    }

    [Fact]
    public async Task Channel_queue_delivers_ids_in_order()
    {
        var sut = new ShowcaseImageQueue();
        var a = ObjectId.GenerateNewId();
        var b = ObjectId.GenerateNewId();

        sut.Enqueue(a);
        sut.Enqueue(b);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        (await sut.DequeueAsync(cts.Token)).Should().Be(a);
        (await sut.DequeueAsync(cts.Token)).Should().Be(b);
    }
}
