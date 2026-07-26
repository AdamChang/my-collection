using FluentAssertions;
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

    private Item _item = null!;

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
        _items.Setup(r => r.UpdateAsync(It.IsAny<Item>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _processor.Setup(p => p.ProcessAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessedImage([1], [2], [3]));

        _storage.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, Stream _, CancellationToken _) => path);

        _storage.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private UploadItemImageCommandHandler CreateUploadSut() =>
        new(_items.Object, _storage.Object, _processor.Object, _time);

    private static UploadItemImageCommand UploadCommand() =>
        new(ItemId.ToString(), new MemoryStream([1, 2, 3]));

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
    public async Task First_upload_becomes_primary_subsequent_ones_do_not()
    {
        var first = await CreateUploadSut().Handle(UploadCommand(), CancellationToken.None);
        var second = await CreateUploadSut().Handle(UploadCommand(), CancellationToken.None);

        first.IsPrimary.Should().BeTrue();
        first.Order.Should().Be(0);
        second.IsPrimary.Should().BeFalse();
        second.Order.Should().Be(1);
        _item.Images.Should().HaveCount(2);
    }

    [Fact]
    public async Task Upload_bumps_item_UpdatedAt()
    {
        await CreateUploadSut().Handle(UploadCommand(), CancellationToken.None);

        _item.UpdatedAt.Should().Be(new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Upload_throws_NotFound_for_unknown_item()
    {
        _items.Setup(r => r.GetAsync(It.IsAny<ObjectId>(), It.IsAny<CancellationToken>())).ReturnsAsync((Item?)null);

        var act = () => CreateUploadSut().Handle(UploadCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Delete_removes_all_three_files_and_promotes_next_primary()
    {
        var first = await CreateUploadSut().Handle(UploadCommand(), CancellationToken.None);
        var second = await CreateUploadSut().Handle(UploadCommand(), CancellationToken.None);

        await new DeleteItemImageCommandHandler(_items.Object, _storage.Object, _time)
            .Handle(new DeleteItemImageCommand(ItemId.ToString(), first.Id), CancellationToken.None);

        _storage.Verify(s => s.DeleteAsync(first.Path, It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync(first.CardPath, It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync(first.ThumbPath, It.IsAny<CancellationToken>()), Times.Once);

        _item.Images.Should().ContainSingle();
        _item.Images[0].Id.Should().Be(second.Id);
        _item.Images[0].IsPrimary.Should().BeTrue("刪掉主圖後應自動晉升下一張");
    }

    [Fact]
    public async Task Delete_throws_NotFound_for_unknown_image()
    {
        var act = () => new DeleteItemImageCommandHandler(_items.Object, _storage.Object, _time)
            .Handle(new DeleteItemImageCommand(ItemId.ToString(), "missing"), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task SetPrimary_moves_the_flag_exclusively()
    {
        var first = await CreateUploadSut().Handle(UploadCommand(), CancellationToken.None);
        var second = await CreateUploadSut().Handle(UploadCommand(), CancellationToken.None);

        await new SetPrimaryImageCommandHandler(_items.Object, _time)
            .Handle(new SetPrimaryImageCommand(ItemId.ToString(), second.Id), CancellationToken.None);

        _item.Images.Single(i => i.Id == first.Id).IsPrimary.Should().BeFalse();
        _item.Images.Single(i => i.Id == second.Id).IsPrimary.Should().BeTrue();
    }
}
