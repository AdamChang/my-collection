using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Application.Media;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class ImageCommandTests
{
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly Mock<IImageProcessor> _processor = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero));

    private static readonly ObjectId ItemId = ObjectId.GenerateNewId();
    private static readonly DateTime Now = new(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc);

    private readonly Item _item;

    public ImageCommandTests()
    {
        _item = new Item
        {
            Id = ItemId,
            OwnerId = ObjectId.GenerateNewId(),
            CategoryId = ObjectId.GenerateNewId(),
            Name = "公仔",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _items.Setup(r => r.GetAsync(ItemId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _item);

        // 模擬資料庫端決定主圖與順序：回傳的是寫入後的圖，不是呼叫端傳入的那張。
        _items.Setup(r => r.AddImageAsync(ItemId, It.IsAny<ItemImage>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ObjectId _, ItemImage image, DateTime _, CancellationToken _) => new ItemImage
            {
                Id = image.Id, Path = image.Path, CardPath = image.CardPath, ThumbPath = image.ThumbPath,
                IsPrimary = true, Order = 0
            });

        _processor.Setup(p => p.ProcessAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessedImage([1], [2], [3]));

        _storage.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, Stream _, CancellationToken _) => path);

        _storage.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private UploadItemImageCommandHandler CreateUploadSut() =>
        new(_items.Object, _storage.Object, _processor.Object, _time, NullLogger<UploadItemImageCommandHandler>.Instance);

    private DeleteItemImageCommandHandler CreateDeleteSut() =>
        new(_items.Object, _storage.Object, _time, NullLogger<DeleteItemImageCommandHandler>.Instance);

    private static UploadItemImageCommand UploadCommand() =>
        new(ItemId.ToString(), new MemoryStream([1, 2, 3]));

    private static ItemImage Image(string id) => new()
    {
        Id = id, Path = $"{id}-full.webp", CardPath = $"{id}-card.webp", ThumbPath = $"{id}-thumb.webp"
    };

    [Fact]
    public async Task Upload_saves_three_files_under_owner_and_item_folder()
    {
        var dto = await CreateUploadSut().Handle(UploadCommand(), CancellationToken.None);

        var prefix = $"{_item.OwnerId}/{ItemId}/{dto.Id}";
        _storage.Verify(s => s.SaveAsync($"{prefix}-full.webp", It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.SaveAsync($"{prefix}-card.webp", It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.SaveAsync($"{prefix}-thumb.webp", It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);

        dto.Path.Should().Be($"{prefix}-full.webp");
        dto.CardPath.Should().Be($"{prefix}-card.webp");
        dto.ThumbPath.Should().Be($"{prefix}-thumb.webp");
    }

    [Fact]
    public async Task Upload_returns_primary_and_order_decided_by_the_repository()
    {
        // 讀到的品項已有圖片，但主圖與順序以原子寫入的結果為準，不以讀到的舊狀態推算。
        _item.Images.Add(Image("existing"));

        var dto = await CreateUploadSut().Handle(UploadCommand(), CancellationToken.None);

        dto.IsPrimary.Should().BeTrue();
        dto.Order.Should().Be(0);
        _items.Verify(r => r.AddImageAsync(ItemId, It.IsAny<ItemImage>(), Now, It.IsAny<CancellationToken>()), Times.Once);
        _items.Verify(r => r.UpdateAsync(It.IsAny<Item>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Upload_throws_NotFound_for_unknown_item_without_saving_files()
    {
        _items.Setup(r => r.GetAsync(It.IsAny<ObjectId>(), It.IsAny<CancellationToken>())).ReturnsAsync((Item?)null);

        var act = () => CreateUploadSut().Handle(UploadCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        _storage.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Upload_removes_saved_files_when_the_database_write_fails_and_keeps_the_original_error()
    {
        _items.Setup(r => r.AddImageAsync(ItemId, It.IsAny<ItemImage>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException(nameof(Item), ItemId));
        _storage.Setup(s => s.DeleteAsync(It.Is<string>(p => p.EndsWith("-card.webp")), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("storage down"));

        var act = () => CreateUploadSut().Handle(UploadCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>("補償刪檔失敗不可蓋掉原始例外");
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Delete_removes_the_files_of_the_image_the_repository_removed()
    {
        var removed = Image("a");
        _items.Setup(r => r.RemoveImageAsync(ItemId, "a", Now, It.IsAny<CancellationToken>())).ReturnsAsync(removed);

        await CreateDeleteSut().Handle(new DeleteItemImageCommand(ItemId.ToString(), "a"), CancellationToken.None);

        _storage.Verify(s => s.DeleteAsync(removed.Path, It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync(removed.CardPath, It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync(removed.ThumbPath, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_succeeds_even_if_a_file_cannot_be_deleted()
    {
        _items.Setup(r => r.RemoveImageAsync(ItemId, "a", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Image("a"));
        _storage.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("storage down"));

        var act = () => CreateDeleteSut().Handle(new DeleteItemImageCommand(ItemId.ToString(), "a"), CancellationToken.None);

        await act.Should().NotThrowAsync("DB 已經不引用這張圖，刪檔失敗只留下孤兒檔");
    }

    [Fact]
    public async Task Delete_does_not_touch_files_when_the_image_is_not_found()
    {
        _items.Setup(r => r.RemoveImageAsync(ItemId, "missing", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException(nameof(ItemImage), "missing"));

        var act = () => CreateDeleteSut().Handle(new DeleteItemImageCommand(ItemId.ToString(), "missing"), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Image_commands_treat_a_malformed_item_id_as_not_found()
    {
        var delete = () => CreateDeleteSut().Handle(new DeleteItemImageCommand("not-an-id", "a"), CancellationToken.None);
        var setPrimary = () => new SetPrimaryImageCommandHandler(_items.Object, _time)
            .Handle(new SetPrimaryImageCommand("not-an-id", "a"), CancellationToken.None);

        await delete.Should().ThrowAsync<NotFoundException>();
        await setPrimary.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task SetPrimary_delegates_to_the_atomic_repository_write()
    {
        await new SetPrimaryImageCommandHandler(_items.Object, _time)
            .Handle(new SetPrimaryImageCommand(ItemId.ToString(), "b"), CancellationToken.None);

        _items.Verify(r => r.SetPrimaryImageAsync(ItemId, "b", Now, It.IsAny<CancellationToken>()), Times.Once);
        _items.Verify(r => r.UpdateAsync(It.IsAny<Item>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
