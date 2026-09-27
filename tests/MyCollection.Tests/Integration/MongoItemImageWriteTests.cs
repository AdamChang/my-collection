using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Application.Media;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

/// <summary>
/// tech debt M1：圖片的新增、刪除、設主圖必須是單一原子寫入，
/// 表單更新則完全不碰 images。用真的 Mongo 驗證，因為缺陷就在「讀取 → 修改 → 整份 $set」這一層。
/// </summary>
[Collection(MongoCollection.Name)]
public class MongoItemImageWriteTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();

    private readonly Mock<IFileStorage> _storage = new();
    private readonly Mock<IImageProcessor> _processor = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 27, 3, 0, 0, TimeSpan.Zero));
    private MongoItemRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        var userContext = new Mock<IUserContext>();
        userContext.SetupGet(c => c.UserId).Returns(Owner);
        _repository = new MongoItemRepository(fixture.Context, userContext.Object);

        _processor.Setup(p => p.ProcessAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessedImage([1], [2], [3]));
        _storage.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, Stream _, CancellationToken _) => path);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Item> SeedAsync(params ItemImage[] images)
    {
        var item = new Item
        {
            Id = ObjectId.GenerateNewId(),
            OwnerId = Owner,
            CategoryId = ObjectId.GenerateNewId(),
            Name = "初音ミク 1/8",
            Source = ItemSource.Manual,
            Images = [.. images],
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await fixture.Context.Items.InsertOneAsync(item);
        return item;
    }

    private static ItemImage Image(string id, bool isPrimary, int order) => new()
    {
        Id = id, Path = $"{id}-full.webp", CardPath = $"{id}-card.webp", ThumbPath = $"{id}-thumb.webp",
        IsPrimary = isPrimary, Order = order
    };

    private async Task<Item> StoredAsync(ObjectId id) =>
        await fixture.Context.Items.Find(Builders<Item>.Filter.Eq(x => x.Id, id)).FirstAsync();

    private Task<ItemImageDto> UploadAsync(Item item) =>
        new UploadItemImageCommandHandler(
                _repository, _storage.Object, _processor.Object, _time,
                NullLogger<UploadItemImageCommandHandler>.Instance)
            .Handle(new UploadItemImageCommand(item.Id.ToString(), new MemoryStream([1])), CancellationToken.None);

    [Fact]
    public async Task Concurrent_uploads_all_survive_with_exactly_one_primary()
    {
        var item = await SeedAsync();

        var uploaded = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => UploadAsync(item)));

        var stored = await StoredAsync(item.Id);
        stored.Images.Select(i => i.Id).Should().BeEquivalentTo(uploaded.Select(d => d.Id), "並行上傳不可互相覆蓋");
        stored.Images.Should().ContainSingle(i => i.IsPrimary);
        stored.Images.Select(i => i.Order).Should().BeEquivalentTo(Enumerable.Range(0, 8));
        uploaded.Should().ContainSingle(d => d.IsPrimary, "回傳的 DTO 要與資料庫寫入後的狀態一致");
        stored.UpdatedAt.Should().Be(_time.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task Upload_to_a_missing_item_removes_the_files_it_just_saved()
    {
        var item = await SeedAsync();
        var handler = new UploadItemImageCommandHandler(
            _repository, _storage.Object, _processor.Object, _time,
            NullLogger<UploadItemImageCommandHandler>.Instance);

        // 讀取之後、寫入之前品項被刪掉：檔案已經存了，DB 卻沒有地方引用它們。
        _processor.Setup(p => p.ProcessAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback(() => fixture.Context.Items.DeleteOne(Builders<Item>.Filter.Eq(x => x.Id, item.Id)))
            .ReturnsAsync(new ProcessedImage([1], [2], [3]));

        var act = () => handler.Handle(
            new UploadItemImageCommand(item.Id.ToString(), new MemoryStream([1])), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Removing_the_primary_promotes_the_first_remaining_and_reorders()
    {
        var item = await SeedAsync(Image("a", true, 0), Image("b", false, 1), Image("c", false, 2));

        var removed = await _repository.RemoveImageAsync(item.Id, "a", _time.GetUtcNow().UtcDateTime, CancellationToken.None);

        removed.Path.Should().Be("a-full.webp", "呼叫端要靠回傳值刪檔");
        var stored = await StoredAsync(item.Id);
        stored.Images.Select(i => (i.Id, i.IsPrimary, i.Order))
            .Should().Equal(("b", true, 0), ("c", false, 1));
    }

    [Fact]
    public async Task Removing_a_secondary_keeps_the_current_primary()
    {
        var item = await SeedAsync(Image("a", false, 0), Image("b", true, 1), Image("c", false, 2));

        await _repository.RemoveImageAsync(item.Id, "a", _time.GetUtcNow().UtcDateTime, CancellationToken.None);

        var stored = await StoredAsync(item.Id);
        stored.Images.Select(i => (i.Id, i.IsPrimary, i.Order))
            .Should().Equal(("b", true, 0), ("c", false, 1));
    }

    [Fact]
    public async Task Delete_handler_writes_the_database_before_deleting_files()
    {
        var item = await SeedAsync(Image("a", true, 0));
        Item? storedWhenDeletingFiles = null;
        _storage.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => storedWhenDeletingFiles ??= StoredAsync(item.Id).GetAwaiter().GetResult())
            .Returns(Task.CompletedTask);

        await new DeleteItemImageCommandHandler(
                _repository, _storage.Object, _time, NullLogger<DeleteItemImageCommandHandler>.Instance)
            .Handle(new DeleteItemImageCommand(item.Id.ToString(), "a"), CancellationToken.None);

        storedWhenDeletingFiles!.Images.Should().BeEmpty("先改 DB 再刪檔：失敗時只留孤兒檔，不會出現破圖（M6）");
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Set_primary_moves_the_flag_exclusively()
    {
        var item = await SeedAsync(Image("a", true, 0), Image("b", false, 1));

        await _repository.SetPrimaryImageAsync(item.Id, "b", _time.GetUtcNow().UtcDateTime, CancellationToken.None);

        var stored = await StoredAsync(item.Id);
        stored.Images.Select(i => (i.Id, i.IsPrimary)).Should().Equal(("a", false), ("b", true));
    }

    [Fact]
    public async Task Image_writes_distinguish_missing_item_from_missing_image()
    {
        var item = await SeedAsync(Image("a", true, 0));
        var now = _time.GetUtcNow().UtcDateTime;

        var missingImage = () => _repository.SetPrimaryImageAsync(item.Id, "zzz", now, CancellationToken.None);
        var missingItem = () => _repository.RemoveImageAsync(ObjectId.GenerateNewId(), "a", now, CancellationToken.None);

        (await missingImage.Should().ThrowAsync<NotFoundException>()).Which.Resource.Should().Be(nameof(ItemImage));
        (await missingItem.Should().ThrowAsync<NotFoundException>()).Which.Resource.Should().Be(nameof(Item));
        (await StoredAsync(item.Id)).Images.Should().ContainSingle(i => i.Id == "a" && i.IsPrimary);
    }

    [Fact]
    public async Task Item_update_never_touches_images()
    {
        // 表單（或任何讀後寫回的呼叫端）手上的 images 是讀取當下的舊陣列，期間上傳了一張圖。
        var stale = await SeedAsync();
        await UploadAsync(stale);
        stale.Name = "新名稱";

        await _repository.UpdateAsync(stale, CancellationToken.None);

        var stored = await StoredAsync(stale.Id);
        stored.Name.Should().Be("新名稱");
        stored.Images.Should().ContainSingle("images 只由 Media 的原子寫入擁有，表單儲存不可覆寫");
    }
}
