# Plan 3：Media + Showcase + Sharing 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **前置：** Plan 1、Plan 2 已完成並全綠。

**Goal:** 實作圖片上傳（ImageSharp 生成 thumb/card/full 三尺寸、`IFileStorage` 抽象）、Showcase 精選牆查詢，以及公開分享連結——公開 API 走獨立唯讀 Handler 與獨立投影，**後端根本不投影** `acquisition`。

**Architecture:** `IFileStorage` 只認相對路徑，`LocalFileStorage` 負責路徑穿越防護；未來換 GCS 只需新增實作並改 `Storage:Provider`。分享頁的資料流完全獨立於內部 `ItemDto`：`MongoPublicCatalogReader` 用 Mongo `$project` 只撈允許的欄位，內部 DTO 之後新增任何欄位都不可能洩漏。

**Tech Stack:** 同前，另加 `SixLabors.ImageSharp` 3.x。

---

## 檔案結構

| 檔案 | 職責 |
|---|---|
| `src/MyCollection.Application/Common/IFileStorage.cs` | 儲存抽象 |
| `src/MyCollection.Infrastructure/Storage/StorageOptions.cs` | `Storage:*` 設定 |
| `src/MyCollection.Infrastructure/Storage/LocalFileStorage.cs` | 本機檔案實作 + 路徑穿越防護 |
| `src/MyCollection.Application/Media/IImageProcessor.cs` | 縮圖抽象 |
| `src/MyCollection.Infrastructure/Imaging/ImageSharpProcessor.cs` | thumb/card/full 生成 |
| `src/MyCollection.Application/Media/ImageCommands.cs` | 上傳 / 刪除 / 設為主圖 |
| `src/MyCollection.Api/Endpoints/MediaEndpoints.cs` | 上傳端點 + `/media/{**path}` 串流 |
| `src/MyCollection.Application/Showcase/GetShowcaseQuery.cs` | 精選牆查詢 |
| `src/MyCollection.Domain/Entities/ShareLink.cs` | 分享連結實體 |
| `src/MyCollection.Application/Sharing/IShareLinkRepository.cs` | |
| `src/MyCollection.Application/Sharing/IPublicCatalogReader.cs` | **獨立投影**契約 |
| `src/MyCollection.Application/Sharing/ShareCommands.cs` `PublicShareQuery.cs` | |
| `src/MyCollection.Infrastructure/Mongo/MongoShareLinkRepository.cs` `MongoPublicCatalogReader.cs` | |
| `src/MyCollection.Api/Endpoints/ShareEndpoints.cs` | `/shares`（需驗證）+ `/public/{slug}`（匿名） |

---

### Task 1：IFileStorage 與 LocalFileStorage

**Files:**
- Create: `src/MyCollection.Application/Common/IFileStorage.cs`
- Create: `src/MyCollection.Infrastructure/Storage/StorageOptions.cs`
- Create: `src/MyCollection.Infrastructure/Storage/LocalFileStorage.cs`
- Test: `tests/MyCollection.Tests/Unit/LocalFileStorageTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/LocalFileStorageTests.cs`：

```csharp
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MyCollection.Infrastructure.Storage;

namespace MyCollection.Tests.Unit;

public class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mycollection-tests", Guid.NewGuid().ToString("N"));

    private LocalFileStorage CreateSut() =>
        new(Options.Create(new StorageOptions { Provider = "Local", LocalRoot = _root }));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static Stream Content(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Save_creates_nested_directories_and_returns_relative_path()
    {
        var sut = CreateSut();

        var path = await sut.SaveAsync("owner1/item1/img-full.webp", Content("hello"), CancellationToken.None);

        path.Should().Be("owner1/item1/img-full.webp");
        File.Exists(Path.Combine(_root, "owner1", "item1", "img-full.webp")).Should().BeTrue();
    }

    [Fact]
    public async Task OpenRead_returns_saved_content()
    {
        var sut = CreateSut();
        await sut.SaveAsync("a/b.txt", Content("hello"), CancellationToken.None);

        await using var stream = await sut.OpenReadAsync("a/b.txt", CancellationToken.None);

        stream.Should().NotBeNull();
        using var reader = new StreamReader(stream!);
        (await reader.ReadToEndAsync()).Should().Be("hello");
    }

    [Fact]
    public async Task OpenRead_returns_null_when_missing()
    {
        (await CreateSut().OpenReadAsync("nope.txt", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Delete_is_idempotent()
    {
        var sut = CreateSut();
        await sut.SaveAsync("a/b.txt", Content("hello"), CancellationToken.None);

        await sut.DeleteAsync("a/b.txt", CancellationToken.None);
        await sut.DeleteAsync("a/b.txt", CancellationToken.None);

        (await sut.OpenReadAsync("a/b.txt", CancellationToken.None)).Should().BeNull();
    }

    [Theory]
    [InlineData("../secrets.txt")]
    [InlineData("a/../../secrets.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("")]
    public async Task Rejects_paths_escaping_the_root(string path)
    {
        var sut = CreateSut();

        var save = () => sut.SaveAsync(path, Content("x"), CancellationToken.None);
        var read = () => sut.OpenReadAsync(path, CancellationToken.None);

        await save.Should().ThrowAsync<ArgumentException>();
        await read.Should().ThrowAsync<ArgumentException>();
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter LocalFileStorageTests`
Expected: 編譯失敗，找不到 `LocalFileStorage` / `StorageOptions`。

- [ ] **Step 3: 實作**

`src/MyCollection.Application/Common/IFileStorage.cs`：

```csharp
namespace MyCollection.Application.Common;

/// <summary>
/// 檔案儲存抽象。所有路徑一律是以 '/' 分隔的相對路徑，實作負責解析成自己的定址方式。
/// 換成 Google Cloud Storage 時只需新增實作並改 Storage:Provider。
/// </summary>
public interface IFileStorage
{
    /// <returns>寫入後的相對路徑（與傳入相同，供呼叫端直接存進文件）。</returns>
    Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct);

    /// <summary>不存在時回傳 null。</summary>
    Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct);

    /// <summary>不存在時不擲例外。</summary>
    Task DeleteAsync(string relativePath, CancellationToken ct);
}
```

`src/MyCollection.Infrastructure/Storage/StorageOptions.cs`：

```csharp
namespace MyCollection.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Local | Gcs（第一版僅實作 Local）。</summary>
    public string Provider { get; init; } = "Local";

    public string LocalRoot { get; init; } = "data/media";
}
```

`src/MyCollection.Infrastructure/Storage/LocalFileStorage.cs`：

```csharp
using Microsoft.Extensions.Options;
using MyCollection.Application.Common;

namespace MyCollection.Infrastructure.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IOptions<StorageOptions> options)
    {
        _root = Path.GetFullPath(options.Value.LocalRoot);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct)
    {
        var fullPath = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var target = File.Create(fullPath);
        await content.CopyToAsync(target, ct);

        return relativePath;
    }

    public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct)
    {
        var fullPath = Resolve(relativePath);

        return Task.FromResult<Stream?>(
            File.Exists(fullPath) ? File.OpenRead(fullPath) : null);
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct)
    {
        var fullPath = Resolve(relativePath);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 把相對路徑解析成根目錄底下的絕對路徑，並拒絕任何逃逸嘗試。
    /// 這是唯一的邊界檢查點，所有公開方法都先走過它。
    /// </summary>
    private string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Path must not be empty.", nameof(relativePath));
        }

        if (Path.IsPathRooted(relativePath) || relativePath.Contains(':'))
        {
            throw new ArgumentException("Path must be relative.", nameof(relativePath));
        }

        var candidate = Path.GetFullPath(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!candidate.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Path escapes the storage root.", nameof(relativePath));
        }

        return candidate;
    }
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter LocalFileStorageTests`
Expected: `Passed: 9`

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(storage): 新增 IFileStorage 抽象與本機實作"
```

---

### Task 2：ImageSharp 縮圖處理

**Files:**
- Create: `src/MyCollection.Application/Media/IImageProcessor.cs`
- Create: `src/MyCollection.Infrastructure/Imaging/ImageSharpProcessor.cs`
- Test: `tests/MyCollection.Tests/Unit/ImageSharpProcessorTests.cs`

- [ ] **Step 1: 安裝套件**

```bash
dotnet add src/MyCollection.Infrastructure package SixLabors.ImageSharp
dotnet add tests/MyCollection.Tests package SixLabors.ImageSharp
```

- [ ] **Step 2: 寫失敗測試**

`tests/MyCollection.Tests/Unit/ImageSharpProcessorTests.cs`：

```csharp
using FluentAssertions;
using MyCollection.Infrastructure.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace MyCollection.Tests.Unit;

public class ImageSharpProcessorTests
{
    private readonly ImageSharpProcessor _sut = new();

    private static Stream PngStream(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task Produces_three_sizes_capped_by_longest_edge()
    {
        await using var source = PngStream(3000, 1500);

        var result = await _sut.ProcessAsync(source, CancellationToken.None);

        Size(result.Full).Should().Be(new Size(1600, 800));
        Size(result.Card).Should().Be(new Size(480, 240));
        Size(result.Thumb).Should().Be(new Size(160, 80));
        return;

        static Size Size(byte[] bytes) => Image.Identify(bytes).Size;
    }

    [Fact]
    public async Task Never_upscales_small_images()
    {
        await using var source = PngStream(100, 50);

        var result = await _sut.ProcessAsync(source, CancellationToken.None);

        Image.Identify(result.Full).Size.Should().Be(new Size(100, 50));
        Image.Identify(result.Card).Size.Should().Be(new Size(100, 50));
        Image.Identify(result.Thumb).Size.Should().Be(new Size(100, 50));
    }

    [Fact]
    public async Task Encodes_every_size_as_webp()
    {
        await using var source = PngStream(800, 600);

        var result = await _sut.ProcessAsync(source, CancellationToken.None);

        Image.DetectFormat(result.Full).Should().BeOfType<WebpFormat>();
        Image.DetectFormat(result.Card).Should().BeOfType<WebpFormat>();
        Image.DetectFormat(result.Thumb).Should().BeOfType<WebpFormat>();
    }

    [Fact]
    public async Task Rejects_non_image_content()
    {
        await using var source = new MemoryStream("not an image"u8.ToArray());

        var act = () => _sut.ProcessAsync(source, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidImageException>();
    }
}
```

- [ ] **Step 3: 跑測試確認失敗**

Run: `dotnet test --filter ImageSharpProcessorTests`
Expected: 編譯失敗，找不到 `ImageSharpProcessor`。

- [ ] **Step 4: 實作**

`src/MyCollection.Application/Media/IImageProcessor.cs`：

```csharp
namespace MyCollection.Application.Media;

public sealed record ProcessedImage(byte[] Full, byte[] Card, byte[] Thumb);

/// <summary>來源不是可解析的圖片。由 GlobalExceptionHandler 之外的驗證層轉成 400。</summary>
public sealed class InvalidImageException(Exception? innerException = null)
    : Exception("The uploaded file is not a valid image.", innerException);

public interface IImageProcessor
{
    /// <summary>生成 full(1600) / card(480) / thumb(160) 三種尺寸，一律輸出 WebP，不放大原圖。</summary>
    Task<ProcessedImage> ProcessAsync(Stream source, CancellationToken ct);
}
```

`InvalidImageException` 需要對應 400。在 `src/MyCollection.Api/GlobalExceptionHandler.cs` 的 `Map` switch 內、`ValidationException` 之後追加一臂：

```csharp
            InvalidImageException i => (StatusCodes.Status400BadRequest, "Invalid image.", i.Message, null),
```

並補 `using MyCollection.Application.Media;`。

`src/MyCollection.Infrastructure/Imaging/ImageSharpProcessor.cs`：

```csharp
using MyCollection.Application.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace MyCollection.Infrastructure.Imaging;

public sealed class ImageSharpProcessor : IImageProcessor
{
    private const int FullMaxEdge = 1600;
    private const int CardMaxEdge = 480;
    private const int ThumbMaxEdge = 160;

    private static readonly WebpEncoder Encoder = new() { Quality = 82 };

    public async Task<ProcessedImage> ProcessAsync(Stream source, CancellationToken ct)
    {
        Image image;
        try
        {
            image = await Image.LoadAsync(source, ct);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            throw new InvalidImageException(ex);
        }

        using (image)
        {
            return new ProcessedImage(
                await ResizeAsync(image, FullMaxEdge, ct),
                await ResizeAsync(image, CardMaxEdge, ct),
                await ResizeAsync(image, ThumbMaxEdge, ct));
        }
    }

    private static async Task<byte[]> ResizeAsync(Image source, int maxEdge, CancellationToken ct)
    {
        using var clone = source.Clone(context =>
        {
            var longest = Math.Max(source.Width, source.Height);
            if (longest <= maxEdge)
            {
                return; // 不放大
            }

            context.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(maxEdge, maxEdge),
                Sampler = KnownResamplers.Lanczos3
            });
        });

        using var buffer = new MemoryStream();
        await clone.SaveAsync(buffer, Encoder, ct);

        return buffer.ToArray();
    }
}
```

- [ ] **Step 5: 跑測試確認通過**

Run: `dotnet test --filter ImageSharpProcessorTests`
Expected: `Passed: 4`

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(media): 新增 ImageSharp 三尺寸縮圖處理"
```

---

### Task 3：圖片上傳 / 刪除 / 設主圖 Command

**Files:**
- Create: `src/MyCollection.Application/Media/ImageCommands.cs`
- Test: `tests/MyCollection.Tests/Unit/ImageCommandTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/ImageCommandTests.cs`：

```csharp
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
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter ImageCommandTests`
Expected: 編譯失敗，找不到 `UploadItemImageCommand` 等型別。

- [ ] **Step 3: 實作**

`src/MyCollection.Application/Media/ImageCommands.cs`：

```csharp
using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Media;

public record UploadItemImageCommand(string ItemId, Stream Content) : IRequest<ItemImageDto>;

public record DeleteItemImageCommand(string ItemId, string ImageId) : IRequest;

public record SetPrimaryImageCommand(string ItemId, string ImageId) : IRequest;

internal static class MediaPaths
{
    public static string Full(Item item, string imageId) => $"{item.OwnerId}/{item.Id}/{imageId}-full.webp";

    public static string Card(Item item, string imageId) => $"{item.OwnerId}/{item.Id}/{imageId}-card.webp";

    public static string Thumb(Item item, string imageId) => $"{item.OwnerId}/{item.Id}/{imageId}-thumb.webp";
}

public sealed class UploadItemImageCommandHandler(
    IItemRepository items,
    IFileStorage storage,
    IImageProcessor imageProcessor,
    TimeProvider timeProvider) : IRequestHandler<UploadItemImageCommand, ItemImageDto>
{
    public async Task<ItemImageDto> Handle(UploadItemImageCommand request, CancellationToken cancellationToken)
    {
        var item = await LoadItemAsync(items, request.ItemId, cancellationToken);

        var processed = await imageProcessor.ProcessAsync(request.Content, cancellationToken);
        var imageId = ObjectId.GenerateNewId().ToString();

        var image = new ItemImage
        {
            Id = imageId,
            Path = await SaveAsync(storage, MediaPaths.Full(item, imageId), processed.Full, cancellationToken),
            CardPath = await SaveAsync(storage, MediaPaths.Card(item, imageId), processed.Card, cancellationToken),
            ThumbPath = await SaveAsync(storage, MediaPaths.Thumb(item, imageId), processed.Thumb, cancellationToken),
            IsPrimary = item.Images.Count == 0,
            Order = item.Images.Count
        };

        item.Images.Add(image);
        item.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await items.UpdateAsync(item, cancellationToken);

        return new ItemImageDto(image.Id, image.Path, image.CardPath, image.ThumbPath, image.IsPrimary, image.Order);
    }

    private static async Task<string> SaveAsync(IFileStorage storage, string path, byte[] content, CancellationToken ct)
    {
        using var stream = new MemoryStream(content);
        return await storage.SaveAsync(path, stream, ct);
    }

    internal static async Task<Item> LoadItemAsync(IItemRepository items, string itemId, CancellationToken ct)
    {
        if (!ObjectId.TryParse(itemId, out var id))
        {
            throw new NotFoundException(nameof(Item), itemId);
        }

        return await items.GetAsync(id, ct) ?? throw new NotFoundException(nameof(Item), itemId);
    }
}

public sealed class DeleteItemImageCommandHandler(
    IItemRepository items,
    IFileStorage storage,
    TimeProvider timeProvider) : IRequestHandler<DeleteItemImageCommand>
{
    public async Task Handle(DeleteItemImageCommand request, CancellationToken cancellationToken)
    {
        var item = await UploadItemImageCommandHandler.LoadItemAsync(items, request.ItemId, cancellationToken);

        var image = item.Images.SingleOrDefault(i => i.Id == request.ImageId)
                    ?? throw new NotFoundException(nameof(ItemImage), request.ImageId);

        await storage.DeleteAsync(image.Path, cancellationToken);
        await storage.DeleteAsync(image.CardPath, cancellationToken);
        await storage.DeleteAsync(image.ThumbPath, cancellationToken);

        item.Images.Remove(image);

        // 主圖被刪掉時晉升下一張，並重排 order
        if (image.IsPrimary && item.Images.Count > 0)
        {
            item.Images[0].IsPrimary = true;
        }

        for (var i = 0; i < item.Images.Count; i++)
        {
            item.Images[i].Order = i;
        }

        item.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await items.UpdateAsync(item, cancellationToken);
    }
}

public sealed class SetPrimaryImageCommandHandler(IItemRepository items, TimeProvider timeProvider)
    : IRequestHandler<SetPrimaryImageCommand>
{
    public async Task Handle(SetPrimaryImageCommand request, CancellationToken cancellationToken)
    {
        var item = await UploadItemImageCommandHandler.LoadItemAsync(items, request.ItemId, cancellationToken);

        if (item.Images.All(i => i.Id != request.ImageId))
        {
            throw new NotFoundException(nameof(ItemImage), request.ImageId);
        }

        foreach (var image in item.Images)
        {
            image.IsPrimary = image.Id == request.ImageId;
        }

        item.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await items.UpdateAsync(item, cancellationToken);
    }
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter ImageCommandTests`
Expected: `Passed: 7`

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(media): 新增圖片上傳、刪除與主圖設定 command"
```

---

### Task 4：Media 端點與 DI

**Files:**
- Create: `src/MyCollection.Api/Endpoints/MediaEndpoints.cs`
- Modify: `src/MyCollection.Infrastructure/DependencyInjection.cs`
- Modify: `src/MyCollection.Api/Program.cs`
- Modify: `src/MyCollection.Api/appsettings.json`
- Test: `tests/MyCollection.Tests/Integration/MediaEndpointsTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Integration/MediaEndpointsTests.cs`：

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MyCollection.Application.Categories;
using MyCollection.Application.Items;
using MyCollection.Tests.Fixtures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MediaEndpointsTests(MongoFixture mongo) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await mongo.ResetAsync();
        _factory = new ApiFactory(mongo);
        _client = await AuthenticatedClient.CreateAsync(_factory, "media@example.com");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static MultipartFormDataContent PngUpload(int width = 800, int height = 600)
    {
        using var image = new Image<Rgba32>(width, height);
        var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder());

        var content = new ByteArrayContent(buffer.ToArray());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");

        return new MultipartFormDataContent { { content, "file", "test.png" } };
    }

    private async Task<ItemDto> CreateItemAsync()
    {
        var category = (await (await _client.PostAsJsonAsync("/categories", new
        {
            name = "公仔", icon = "figure", kind = "Physical",
            fields = Array.Empty<object>()
        })).Content.ReadFromJsonAsync<CategoryDto>())!;

        return (await (await _client.PostAsJsonAsync("/items", new
        {
            categoryId = category.Id, name = "公仔", description = (string?)null,
            tags = Array.Empty<string>(), isShowcased = false,
            attributes = new { }, acquisition = (object?)null
        })).Content.ReadFromJsonAsync<ItemDto>())!;
    }

    [Fact]
    public async Task Upload_then_fetch_media_returns_webp()
    {
        var item = await CreateItemAsync();

        var uploaded = await _client.PostAsync($"/items/{item.Id}/images", PngUpload());
        uploaded.StatusCode.Should().Be(HttpStatusCode.Created);
        var image = (await uploaded.Content.ReadFromJsonAsync<ItemImageDto>())!;

        var media = await _client.GetAsync($"/media/{image.ThumbPath}");

        media.StatusCode.Should().Be(HttpStatusCode.OK);
        media.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
        (await media.Content.ReadAsByteArrayAsync()).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Uploaded_image_appears_on_the_item()
    {
        var item = await CreateItemAsync();
        await _client.PostAsync($"/items/{item.Id}/images", PngUpload());

        var reloaded = await _client.GetFromJsonAsync<ItemDto>($"/items/{item.Id}");

        reloaded!.Images.Should().ContainSingle().Which.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task Upload_of_non_image_returns_400()
    {
        var item = await CreateItemAsync();

        var content = new ByteArrayContent("not an image"u8.ToArray());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        using var form = new MultipartFormDataContent { { content, "file", "fake.png" } };

        var response = await _client.PostAsync($"/items/{item.Id}/images", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_image_removes_it_from_item_and_storage()
    {
        var item = await CreateItemAsync();
        var image = (await (await _client.PostAsync($"/items/{item.Id}/images", PngUpload()))
            .Content.ReadFromJsonAsync<ItemImageDto>())!;

        var deleted = await _client.DeleteAsync($"/items/{item.Id}/images/{image.Id}");

        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetFromJsonAsync<ItemDto>($"/items/{item.Id}"))!.Images.Should().BeEmpty();
        (await _client.GetAsync($"/media/{image.ThumbPath}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Media_endpoint_rejects_path_traversal()
    {
        var response = await _client.GetAsync("/media/..%2F..%2Fappsettings.json");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_to_another_users_item_returns_404()
    {
        var item = await CreateItemAsync();
        using var intruder = await AuthenticatedClient.CreateAsync(_factory, "intruder-media@example.com");

        var response = await intruder.PostAsync($"/items/{item.Id}/images", PngUpload());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter MediaEndpointsTests`
Expected: 全部 FAIL（端點 404）。

- [ ] **Step 3: 實作端點**

`src/MyCollection.Api/Endpoints/MediaEndpoints.cs`：

```csharp
using MediatR;
using MyCollection.Application.Common;
using MyCollection.Application.Media;

namespace MyCollection.Api.Endpoints;

public static class MediaEndpoints
{
    /// <summary>單張圖片上傳大小上限（10 MB）。</summary>
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/items/{itemId}/images").WithTags("Media").RequireAuthorization();

        group.MapPost("/", async (string itemId, IFormFile file, ISender sender, CancellationToken ct) =>
            {
                if (file.Length is 0 or > MaxUploadBytes)
                {
                    return Results.BadRequest(new { title = "File must be between 1 byte and 10 MB." });
                }

                await using var stream = file.OpenReadStream();
                var image = await sender.Send(new UploadItemImageCommand(itemId, stream), ct);

                return Results.Created($"/media/{image.Path}", image);
            })
            .DisableAntiforgery();

        group.MapDelete("/{imageId}", async (string itemId, string imageId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteItemImageCommand(itemId, imageId), ct);
            return Results.NoContent();
        });

        group.MapPost("/{imageId}/primary", async (string itemId, string imageId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new SetPrimaryImageCommand(itemId, imageId), ct);
            return Results.NoContent();
        });

        // 匿名：分享頁需要讀得到圖片。路徑本身含 ObjectId，難以枚舉。
        app.MapGet("/media/{**path}", async (string path, IFileStorage storage, CancellationToken ct) =>
            {
                Stream? stream;
                try
                {
                    stream = await storage.OpenReadAsync(path, ct);
                }
                catch (ArgumentException)
                {
                    return Results.NotFound();
                }

                return stream is null
                    ? Results.NotFound()
                    : Results.Stream(stream, "image/webp");
            })
            .AllowAnonymous()
            .WithTags("Media");

        return app;
    }
}
```

- [ ] **Step 4: 註冊 DI 與設定**

`src/MyCollection.Infrastructure/DependencyInjection.cs` 追加：

```csharp
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IImageProcessor, ImageSharpProcessor>();
```

補 `using MyCollection.Application.Media;`、`using MyCollection.Infrastructure.Imaging;`、`using MyCollection.Infrastructure.Storage;`。

`src/MyCollection.Api/Program.cs` 追加 `app.MapMediaEndpoints();`。

`src/MyCollection.Api/appsettings.json` 追加：

```json
  "Storage": {
    "Provider": "Local",
    "LocalRoot": "data/media"
  }
```

整合測試每次跑會寫入 `data/media`；在 `tests/MyCollection.Tests/Fixtures/ApiFactory.cs` 的設定字典追加一行，讓測試寫到暫存目錄：

```csharp
            ["Storage:LocalRoot"] = Path.Combine(Path.GetTempPath(), "mycollection-tests", Guid.NewGuid().ToString("N")),
```

- [ ] **Step 5: 跑測試確認通過**

Run: `dotnet test --filter MediaEndpointsTests`
Expected: `Passed: 6`

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(api): 新增圖片上傳與媒體串流端點"
```

---

### Task 5：Showcase 查詢

**Files:**
- Create: `src/MyCollection.Application/Showcase/GetShowcaseQuery.cs`
- Create: `src/MyCollection.Api/Endpoints/ShowcaseEndpoints.cs`
- Modify: `src/MyCollection.Api/Program.cs`
- Test: `tests/MyCollection.Tests/Unit/ShowcaseQueryTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/ShowcaseQueryTests.cs`：

```csharp
using FluentAssertions;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Application.Showcase;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class ShowcaseQueryTests
{
    private readonly Mock<IItemRepository> _items = new();

    [Fact]
    public async Task Always_filters_to_showcased_items_regardless_of_caller_input()
    {
        ItemQuerySpec? captured = null;
        _items.Setup(r => r.SearchAsync(It.IsAny<ItemQuerySpec>(), It.IsAny<CancellationToken>()))
            .Callback<ItemQuerySpec, CancellationToken>((s, _) => captured = s)
            .ReturnsAsync(new PagedResult<Item>([], 0, 1, 24));

        await new GetShowcaseQueryHandler(_items.Object).Handle(new GetShowcaseQuery(2, 12), CancellationToken.None);

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

        var result = await new GetShowcaseQueryHandler(_items.Object)
            .Handle(new GetShowcaseQuery(), CancellationToken.None);

        result.Total.Should().Be(1);
        result.Items.Should().ContainSingle().Which.Name.Should().Be("Portal 2");
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
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter ShowcaseQueryTests`
Expected: 編譯失敗，找不到 `GetShowcaseQuery`。

- [ ] **Step 3: 實作**

`src/MyCollection.Application/Showcase/GetShowcaseQuery.cs`：

```csharp
using FluentValidation;
using MediatR;
using MyCollection.Application.Common;
using MyCollection.Application.Items;

namespace MyCollection.Application.Showcase;

/// <summary>首頁精選牆：跨品類混合，只顯示 isShowcased。</summary>
public record GetShowcaseQuery(int Page = 1, int PageSize = 24) : IRequest<PagedResult<ItemDto>>;

public sealed class GetShowcaseQueryValidator : AbstractValidator<GetShowcaseQuery>
{
    public GetShowcaseQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

public sealed class GetShowcaseQueryHandler(IItemRepository items)
    : IRequestHandler<GetShowcaseQuery, PagedResult<ItemDto>>
{
    public async Task<PagedResult<ItemDto>> Handle(GetShowcaseQuery request, CancellationToken cancellationToken)
    {
        var result = await items.SearchAsync(
            new ItemQuerySpec { IsShowcased = true, Page = request.Page, PageSize = request.PageSize },
            cancellationToken);

        return new PagedResult<ItemDto>(
            result.Items.Select(ItemMapper.ToDto).ToArray(),
            result.Total,
            result.Page,
            result.PageSize);
    }
}
```

`src/MyCollection.Api/Endpoints/ShowcaseEndpoints.cs`：

```csharp
using MediatR;
using MyCollection.Application.Showcase;

namespace MyCollection.Api.Endpoints;

public static class ShowcaseEndpoints
{
    public static IEndpointRouteBuilder MapShowcaseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/showcase", async (int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetShowcaseQuery(page ?? 1, pageSize ?? 24), ct)))
            .RequireAuthorization()
            .WithTags("Showcase");

        return app;
    }
}
```

`src/MyCollection.Api/Program.cs` 追加 `app.MapShowcaseEndpoints();`。

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter ShowcaseQueryTests`
Expected: `Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(showcase): 新增精選牆查詢與端點"
```

---

### Task 6：ShareLink 實體與 Repository

**Files:**
- Create: `src/MyCollection.Domain/Entities/ShareLink.cs`
- Create: `src/MyCollection.Application/Sharing/IShareLinkRepository.cs`
- Create: `src/MyCollection.Infrastructure/Mongo/MongoShareLinkRepository.cs`
- Modify: `src/MyCollection.Infrastructure/Mongo/MongoContext.cs`、`MongoIndexInitializer.cs`
- Modify: `tests/MyCollection.Tests/Fixtures/MongoFixture.cs`
- Test: `tests/MyCollection.Tests/Integration/MongoShareLinkRepositoryTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Integration/MongoShareLinkRepositoryTests.cs`：

```csharp
using FluentAssertions;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoShareLinkRepositoryTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly ObjectId OtherOwner = ObjectId.GenerateNewId();

    private MongoShareLinkRepository _sut = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        var userContext = new Mock<IUserContext>();
        userContext.SetupGet(c => c.UserId).Returns(Owner);
        _sut = new MongoShareLinkRepository(fixture.Context, userContext.Object);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static ShareLink NewLink(ObjectId ownerId, string slug) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = ownerId,
        Slug = slug,
        Scope = ShareScope.Showcase,
        IncludeCategoryIds = [],
        IncludePrice = false,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Insert_then_GetBySlug_ignores_owner()
    {
        await _sut.InsertAsync(NewLink(Owner, "abc123"), CancellationToken.None);

        // 公開查詢不帶 ownerId
        var found = await _sut.GetBySlugAsync("abc123", CancellationToken.None);

        found.Should().NotBeNull();
        found!.OwnerId.Should().Be(Owner);
    }

    [Fact]
    public async Task Insert_duplicate_slug_throws_ConflictException()
    {
        await _sut.InsertAsync(NewLink(Owner, "abc123"), CancellationToken.None);

        var act = () => _sut.InsertAsync(NewLink(Owner, "abc123"), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task ListAsync_returns_own_links_only()
    {
        await _sut.InsertAsync(NewLink(Owner, "mine"), CancellationToken.None);
        await fixture.Context.ShareLinks.InsertOneAsync(NewLink(OtherOwner, "theirs"));

        var links = await _sut.ListAsync(CancellationToken.None);

        links.Should().ContainSingle().Which.Slug.Should().Be("mine");
    }

    [Fact]
    public async Task DeleteAsync_throws_NotFound_for_other_owners_link()
    {
        var foreign = NewLink(OtherOwner, "theirs");
        await fixture.Context.ShareLinks.InsertOneAsync(foreign);

        var act = () => _sut.DeleteAsync(foreign.Id, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter MongoShareLinkRepositoryTests`
Expected: 編譯失敗，找不到 `ShareLink` / `MongoShareLinkRepository`。

- [ ] **Step 3: 實作實體**

`src/MyCollection.Domain/Entities/ShareLink.cs`：

```csharp
using MongoDB.Bson;

namespace MyCollection.Domain.Entities;

public enum ShareScope
{
    /// <summary>只輸出 isShowcased = true 的品項。</summary>
    Showcase,

    /// <summary>輸出 IncludeCategoryIds 指定品類的全部品項。</summary>
    Category
}

public sealed class ShareLink
{
    public ObjectId Id { get; set; }
    public ObjectId OwnerId { get; set; }

    /// <summary>公開網址的識別碼，全域唯一。</summary>
    public required string Slug { get; set; }

    public ShareScope Scope { get; set; } = ShareScope.Showcase;
    public List<ObjectId> IncludeCategoryIds { get; set; } = [];

    /// <summary>預設 false。true 時公開投影才會額外納入 acquisition.price。</summary>
    public bool IncludePrice { get; set; }

    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 4: 實作 Repository 與索引**

`src/MyCollection.Application/Sharing/IShareLinkRepository.cs`：

```csharp
using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Sharing;

public interface IShareLinkRepository
{
    Task<IReadOnlyList<ShareLink>> ListAsync(CancellationToken ct);

    /// <summary>公開查詢用，刻意不套 ownerId 過濾。</summary>
    Task<ShareLink?> GetBySlugAsync(string slug, CancellationToken ct);

    /// <summary>slug 重複時擲 ConflictException。</summary>
    Task InsertAsync(ShareLink link, CancellationToken ct);

    Task DeleteAsync(ObjectId id, CancellationToken ct);
}
```

`src/MyCollection.Infrastructure/Mongo/MongoShareLinkRepository.cs`：

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Common;
using MyCollection.Application.Sharing;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoShareLinkRepository(MongoContext context, IUserContext userContext) : IShareLinkRepository
{
    private static readonly FilterDefinitionBuilder<ShareLink> Filter = Builders<ShareLink>.Filter;

    private IMongoCollection<ShareLink> Links => context.ShareLinks;

    public async Task<IReadOnlyList<ShareLink>> ListAsync(CancellationToken ct) =>
        await Links
            .Find(Filter.Eq(x => x.OwnerId, userContext.UserId))
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

    public Task<ShareLink?> GetBySlugAsync(string slug, CancellationToken ct) =>
        Links.Find(Filter.Eq(x => x.Slug, slug)).FirstOrDefaultAsync(ct)!;

    public async Task InsertAsync(ShareLink link, CancellationToken ct)
    {
        link.OwnerId = userContext.UserId;

        try
        {
            await Links.InsertOneAsync(link, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new ConflictException($"Share slug '{link.Slug}' is already taken.");
        }
    }

    public async Task DeleteAsync(ObjectId id, CancellationToken ct)
    {
        var result = await Links.DeleteOneAsync(
            Filter.And(Filter.Eq(x => x.Id, id), Filter.Eq(x => x.OwnerId, userContext.UserId)), ct);

        if (result.DeletedCount == 0)
        {
            throw new NotFoundException(nameof(ShareLink), id);
        }
    }
}
```

`src/MyCollection.Infrastructure/Mongo/MongoContext.cs` 追加：

```csharp
    public IMongoCollection<ShareLink> ShareLinks => Database.GetCollection<ShareLink>("shareLinks");
```

`src/MyCollection.Infrastructure/Mongo/MongoIndexInitializer.cs` 追加：

```csharp
        await context.ShareLinks.Indexes.CreateOneAsync(
            new CreateIndexModel<ShareLink>(
                Builders<ShareLink>.IndexKeys.Ascending(x => x.Slug),
                new CreateIndexOptions { Name = "ux_shareLinks_slug", Unique = true }),
            cancellationToken: ct);
```

`tests/MyCollection.Tests/Fixtures/MongoFixture.cs` 的 `ResetAsync` 追加：

```csharp
        await Context.ShareLinks.DeleteManyAsync(FilterDefinition<Domain.Entities.ShareLink>.Empty);
```

`src/MyCollection.Infrastructure/DependencyInjection.cs` 追加：

```csharp
        services.AddScoped<IShareLinkRepository, MongoShareLinkRepository>();
```

- [ ] **Step 5: 跑測試確認通過**

Run: `dotnet test --filter MongoShareLinkRepositoryTests`
Expected: `Passed: 4`

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(sharing): 新增 ShareLink 實體與 repository"
```

---

### Task 7：公開投影 Reader

**Files:**
- Create: `src/MyCollection.Application/Sharing/IPublicCatalogReader.cs`
- Create: `src/MyCollection.Infrastructure/Mongo/MongoPublicCatalogReader.cs`
- Test: `tests/MyCollection.Tests/Integration/MongoPublicCatalogReaderTests.cs`

這是分享頁不洩漏購入價的關鍵：`acquisition` 在 Mongo `$project` 階段就被排除，不存在於任何回傳物件上。

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Integration/MongoPublicCatalogReaderTests.cs`：

```csharp
using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Application.Sharing;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoPublicCatalogReaderTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly ObjectId OtherOwner = ObjectId.GenerateNewId();
    private static readonly ObjectId FigureCategory = ObjectId.GenerateNewId();
    private static readonly ObjectId GameCategory = ObjectId.GenerateNewId();

    private MongoPublicCatalogReader _sut = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        _sut = new MongoPublicCatalogReader(fixture.Context);
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Item NewItem(ObjectId ownerId, string name, ObjectId categoryId, bool showcased) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = ownerId,
        CategoryId = categoryId,
        Name = name,
        Description = "描述",
        Tags = ["tag"],
        IsShowcased = showcased,
        Attributes = new BsonDocument("brand", "GSC"),
        Acquisition = new Acquisition
        {
            AcquiredAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Price = new Money(12800m, "TWD"),
            Vendor = "GSC 官網"
        },
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private async Task SeedAsync()
    {
        await fixture.Context.Items.InsertManyAsync(
        [
            NewItem(Owner, "精選公仔", FigureCategory, showcased: true),
            NewItem(Owner, "非精選公仔", FigureCategory, showcased: false),
            NewItem(Owner, "精選遊戲", GameCategory, showcased: true),
            NewItem(OtherOwner, "別人的精選", FigureCategory, showcased: true)
        ]);

        await fixture.Context.Categories.InsertManyAsync(
        [
            new Category { Id = FigureCategory, OwnerId = Owner, Name = "公仔", Icon = "figure" },
            new Category { Id = GameCategory, OwnerId = Owner, Name = "數位遊戲", Icon = "game" }
        ]);
    }

    [Fact]
    public async Task Showcase_scope_returns_only_showcased_items_of_that_owner()
    {
        var items = await _sut.ListItemsAsync(Owner, ShareScope.Showcase, [], includePrice: false, CancellationToken.None);

        items.Select(i => i.Name).Should().BeEquivalentTo("精選公仔", "精選遊戲");
    }

    [Fact]
    public async Task Category_scope_returns_all_items_of_the_listed_categories()
    {
        var items = await _sut.ListItemsAsync(Owner, ShareScope.Category, [FigureCategory], includePrice: false, CancellationToken.None);

        items.Select(i => i.Name).Should().BeEquivalentTo("精選公仔", "非精選公仔");
    }

    [Fact]
    public async Task Projection_excludes_acquisition_entirely_when_price_not_included()
    {
        var items = await _sut.ListItemsAsync(Owner, ShareScope.Showcase, [], includePrice: false, CancellationToken.None);

        items.Should().OnlyContain(i => i.Price == null);
        items.Should().OnlyContain(i => i.Name.Length > 0);
    }

    [Fact]
    public async Task Projection_includes_price_only_when_explicitly_enabled()
    {
        var items = await _sut.ListItemsAsync(Owner, ShareScope.Showcase, [], includePrice: true, CancellationToken.None);

        items.Should().OnlyContain(i => i.Price != null);
        items[0].Price!.Amount.Should().Be(12800m);
        items[0].Price.Currency.Should().Be("TWD");
    }

    [Fact]
    public async Task ListCategoryNamesAsync_maps_ids_to_names()
    {
        var names = await _sut.ListCategoryNamesAsync(Owner, CancellationToken.None);

        names[FigureCategory].Should().Be("公仔");
        names[GameCategory].Should().Be("數位遊戲");
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter MongoPublicCatalogReaderTests`
Expected: 編譯失敗，找不到 `MongoPublicCatalogReader`。

- [ ] **Step 3: 實作**

`src/MyCollection.Application/Sharing/IPublicCatalogReader.cs`：

```csharp
using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Sharing;

/// <summary>
/// 公開分享頁專用的投影結果。刻意不含 Acquisition 的 AcquiredAt 與 Vendor，
/// Price 只有在 ShareLink.IncludePrice 為 true 時才被投影出來。
/// </summary>
public sealed class PublicItemProjection
{
    public ObjectId Id { get; set; }
    public ObjectId CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<ItemImage> Images { get; set; } = [];
    public BsonDocument Attributes { get; set; } = [];
    public Money? Price { get; set; }
}

public interface IPublicCatalogReader
{
    /// <summary>刻意接受明確的 ownerId：這條路徑不經過 IUserContext（呼叫端是匿名的）。</summary>
    Task<IReadOnlyList<PublicItemProjection>> ListItemsAsync(
        ObjectId ownerId,
        ShareScope scope,
        IReadOnlyList<ObjectId> categoryIds,
        bool includePrice,
        CancellationToken ct);

    Task<IReadOnlyDictionary<ObjectId, string>> ListCategoryNamesAsync(ObjectId ownerId, CancellationToken ct);
}
```

`src/MyCollection.Infrastructure/Mongo/MongoPublicCatalogReader.cs`：

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Sharing;
using MyCollection.Domain.Entities;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoPublicCatalogReader(MongoContext context) : IPublicCatalogReader
{
    private static readonly FilterDefinitionBuilder<Item> Filter = Builders<Item>.Filter;

    /// <summary>
    /// 白名單投影。內部 Item 之後新增任何欄位都不會自動出現在公開回應上——
    /// 必須有人主動把它加進這裡。
    /// </summary>
    private static readonly ProjectionDefinition<Item> BaseProjection = Builders<Item>.Projection
        .Include(x => x.CategoryId)
        .Include(x => x.Name)
        .Include(x => x.Description)
        .Include(x => x.Tags)
        .Include(x => x.Images)
        .Include(x => x.Attributes);

    public async Task<IReadOnlyList<PublicItemProjection>> ListItemsAsync(
        ObjectId ownerId,
        ShareScope scope,
        IReadOnlyList<ObjectId> categoryIds,
        bool includePrice,
        CancellationToken ct)
    {
        var filters = new List<FilterDefinition<Item>> { Filter.Eq(x => x.OwnerId, ownerId) };

        if (scope == ShareScope.Showcase)
        {
            filters.Add(Filter.Eq(x => x.IsShowcased, true));
        }
        else
        {
            filters.Add(categoryIds.Count > 0
                ? Filter.In(x => x.CategoryId, categoryIds)
                : Filter.Where(_ => false));
        }

        var projection = includePrice
            ? BaseProjection.Include("acquisition.price")
            : BaseProjection;

        var documents = await context.Items
            .Find(Filter.And(filters))
            .Project(projection)
            .SortByDescending(x => x.UpdatedAt)
            .ToListAsync(ct);

        return documents.Select(ToProjection).ToArray();
    }

    public async Task<IReadOnlyDictionary<ObjectId, string>> ListCategoryNamesAsync(ObjectId ownerId, CancellationToken ct)
    {
        var categories = await context.Categories
            .Find(Builders<Category>.Filter.In(x => x.OwnerId, [ownerId, null]))
            .Project(Builders<Category>.Projection.Include(x => x.Name))
            .ToListAsync(ct);

        return categories.ToDictionary(
            d => d["_id"].AsObjectId,
            d => d.GetValue("name", BsonString.Empty).AsString);
    }

    private static PublicItemProjection ToProjection(BsonDocument document) => new()
    {
        Id = document["_id"].AsObjectId,
        CategoryId = document.GetValue("categoryId", BsonNull.Value) is { IsBsonNull: false } c ? c.AsObjectId : ObjectId.Empty,
        Name = document.GetValue("name", BsonString.Empty).AsString,
        Description = document.GetValue("description", BsonNull.Value) is { IsBsonNull: false } d ? d.AsString : null,
        Tags = document.GetValue("tags", new BsonArray()).AsBsonArray.Select(t => t.AsString).ToList(),
        Images = document.GetValue("images", new BsonArray()).AsBsonArray
            .Select(i => MongoDB.Bson.Serialization.BsonSerializer.Deserialize<ItemImage>(i.AsBsonDocument))
            .ToList(),
        Attributes = document.GetValue("attributes", new BsonDocument()).AsBsonDocument,
        Price = document.GetValue("acquisition", BsonNull.Value) is { IsBsonNull: false } a
                && a.AsBsonDocument.GetValue("price", BsonNull.Value) is { IsBsonNull: false } p
            ? MongoDB.Bson.Serialization.BsonSerializer.Deserialize<Money>(p.AsBsonDocument)
            : null
    };
}
```

`src/MyCollection.Infrastructure/DependencyInjection.cs` 追加：

```csharp
        services.AddScoped<IPublicCatalogReader, MongoPublicCatalogReader>();
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter MongoPublicCatalogReaderTests`
Expected: `Passed: 5`

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(sharing): 新增公開投影 reader，acquisition 不進入投影"
```

---

### Task 8：分享 Command / Query 與端點

**Files:**
- Create: `src/MyCollection.Application/Sharing/ShareDtos.cs`
- Create: `src/MyCollection.Application/Sharing/ShareCommands.cs`
- Create: `src/MyCollection.Application/Sharing/GetPublicShareQuery.cs`
- Create: `src/MyCollection.Api/Endpoints/ShareEndpoints.cs`
- Modify: `src/MyCollection.Api/Program.cs`、`src/MyCollection.Infrastructure/DependencyInjection.cs`
- Test: `tests/MyCollection.Tests/Unit/ShareCommandTests.cs`
- Test: `tests/MyCollection.Tests/Integration/ShareEndpointsTests.cs`

- [ ] **Step 1: 寫失敗單元測試**

`tests/MyCollection.Tests/Unit/ShareCommandTests.cs`：

```csharp
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Auth;
using MyCollection.Application.Sharing;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class ShareCommandTests
{
    private readonly Mock<IShareLinkRepository> _links = new();
    private readonly Mock<IPublicCatalogReader> _catalog = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero));

    private static readonly ObjectId Owner = ObjectId.GenerateNewId();

    [Fact]
    public async Task Create_generates_url_safe_slug()
    {
        ShareLink? saved = null;
        _links.Setup(r => r.InsertAsync(It.IsAny<ShareLink>(), It.IsAny<CancellationToken>()))
            .Callback<ShareLink, CancellationToken>((l, _) => saved = l)
            .Returns(Task.CompletedTask);

        var dto = await new CreateShareLinkCommandHandler(_links.Object, _time)
            .Handle(new CreateShareLinkCommand("Showcase", [], false, null), CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.Slug.Should().MatchRegex("^[A-Za-z0-9]{12}$");
        saved.Scope.Should().Be(ShareScope.Showcase);
        saved.CreatedAt.Should().Be(new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc));
        dto.Slug.Should().Be(saved.Slug);
    }

    [Fact]
    public void Validator_requires_categories_for_category_scope()
    {
        var validator = new CreateShareLinkCommandValidator();

        validator.Validate(new CreateShareLinkCommand("Category", [], false, null)).IsValid.Should().BeFalse();
        validator.Validate(new CreateShareLinkCommand("Category", [ObjectId.GenerateNewId().ToString()], false, null))
            .IsValid.Should().BeTrue();
        validator.Validate(new CreateShareLinkCommand("Nope", [], false, null)).IsValid.Should().BeFalse();
    }

    private GetPublicShareQueryHandler CreatePublicSut() =>
        new(_links.Object, _catalog.Object, _users.Object, _time);

    private static ShareLink Link(DateTime? expiresAt = null, bool includePrice = false) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = Owner,
        Slug = "abc123abc123",
        Scope = ShareScope.Showcase,
        IncludeCategoryIds = [],
        IncludePrice = includePrice,
        ExpiresAt = expiresAt,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Public_query_throws_NotFound_for_unknown_slug()
    {
        _links.Setup(r => r.GetBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShareLink?)null);

        var act = () => CreatePublicSut().Handle(new GetPublicShareQuery("nope"), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Public_query_throws_NotFound_for_expired_link()
    {
        _links.Setup(r => r.GetBySlugAsync("abc123abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Link(expiresAt: new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)));

        var act = () => CreatePublicSut().Handle(new GetPublicShareQuery("abc123abc123"), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Public_query_passes_includePrice_flag_through()
    {
        _links.Setup(r => r.GetBySlugAsync("abc123abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Link(includePrice: true));
        _users.Setup(r => r.GetByIdAsync(Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Email = "a@b.c", PasswordHash = "h", DisplayName = "Adam" });
        _catalog.Setup(r => r.ListItemsAsync(Owner, ShareScope.Showcase, It.IsAny<IReadOnlyList<ObjectId>>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PublicItemProjection
            {
                Id = ObjectId.GenerateNewId(),
                CategoryId = ObjectId.GenerateNewId(),
                Name = "精選公仔",
                Price = new Money(12800m, "TWD")
            }]);
        _catalog.Setup(r => r.ListCategoryNamesAsync(Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<ObjectId, string>());

        var result = await CreatePublicSut().Handle(new GetPublicShareQuery("abc123abc123"), CancellationToken.None);

        result.OwnerDisplayName.Should().Be("Adam");
        result.Items.Should().ContainSingle().Which.Price!.Amount.Should().Be(12800m);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter ShareCommandTests`
Expected: 編譯失敗，找不到 `CreateShareLinkCommand` 等型別。

- [ ] **Step 3: 實作 DTO 與 Command**

`src/MyCollection.Application/Sharing/ShareDtos.cs`：

```csharp
namespace MyCollection.Application.Sharing;

public record ShareLinkDto(
    string Id,
    string Slug,
    string Scope,
    IReadOnlyList<string> IncludeCategoryIds,
    bool IncludePrice,
    DateTime? ExpiresAt,
    DateTime CreatedAt);

public record PublicImageDto(string CardPath, string ThumbPath, bool IsPrimary, int Order);

public record PublicPriceDto(decimal Amount, string Currency);

/// <summary>
/// 公開分享頁專用 DTO。刻意不共用 ItemDto——內部 DTO 新增欄位時不可能意外洩漏。
/// </summary>
public record PublicItemDto(
    string Id,
    string Name,
    string? Description,
    string CategoryName,
    IReadOnlyList<string> Tags,
    IReadOnlyList<PublicImageDto> Images,
    IReadOnlyDictionary<string, object?> Attributes,
    PublicPriceDto? Price);

public record PublicShareDto(
    string OwnerDisplayName,
    string Scope,
    IReadOnlyList<PublicItemDto> Items);
```

`src/MyCollection.Application/Sharing/ShareCommands.cs`：

```csharp
using System.Security.Cryptography;
using FluentValidation;
using MediatR;
using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Sharing;

public record CreateShareLinkCommand(
    string Scope,
    IReadOnlyList<string> IncludeCategoryIds,
    bool IncludePrice,
    DateTime? ExpiresAt) : IRequest<ShareLinkDto>;

public record ListShareLinksQuery : IRequest<IReadOnlyList<ShareLinkDto>>;

public record DeleteShareLinkCommand(string Id) : IRequest;

public sealed class CreateShareLinkCommandValidator : AbstractValidator<CreateShareLinkCommand>
{
    public CreateShareLinkCommandValidator()
    {
        RuleFor(x => x.Scope)
            .Must(s => Enum.TryParse<ShareScope>(s, ignoreCase: true, out _))
            .WithMessage("Scope must be 'Showcase' or 'Category'.");

        RuleFor(x => x.IncludeCategoryIds)
            .NotEmpty()
            .When(x => string.Equals(x.Scope, nameof(ShareScope.Category), StringComparison.OrdinalIgnoreCase))
            .WithMessage("Category scope requires at least one category.");

        RuleForEach(x => x.IncludeCategoryIds)
            .Must(id => ObjectId.TryParse(id, out _))
            .WithMessage("Invalid category id.");
    }
}

public static class ShareMapper
{
    public static ShareLinkDto ToDto(ShareLink link) => new(
        link.Id.ToString(),
        link.Slug,
        link.Scope.ToString(),
        link.IncludeCategoryIds.Select(id => id.ToString()).ToArray(),
        link.IncludePrice,
        link.ExpiresAt,
        link.CreatedAt);
}

public sealed class CreateShareLinkCommandHandler(IShareLinkRepository links, TimeProvider timeProvider)
    : IRequestHandler<CreateShareLinkCommand, ShareLinkDto>
{
    private const string SlugAlphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int SlugLength = 12;

    public async Task<ShareLinkDto> Handle(CreateShareLinkCommand request, CancellationToken cancellationToken)
    {
        var link = new ShareLink
        {
            Id = ObjectId.GenerateNewId(),
            Slug = GenerateSlug(),
            Scope = Enum.Parse<ShareScope>(request.Scope, ignoreCase: true),
            IncludeCategoryIds = request.IncludeCategoryIds.Select(ObjectId.Parse).ToList(),
            IncludePrice = request.IncludePrice,
            ExpiresAt = request.ExpiresAt,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };

        await links.InsertAsync(link, cancellationToken);

        return ShareMapper.ToDto(link);
    }

    private static string GenerateSlug() =>
        RandomNumberGenerator.GetString(SlugAlphabet, SlugLength);
}

public sealed class ListShareLinksQueryHandler(IShareLinkRepository links)
    : IRequestHandler<ListShareLinksQuery, IReadOnlyList<ShareLinkDto>>
{
    public async Task<IReadOnlyList<ShareLinkDto>> Handle(ListShareLinksQuery request, CancellationToken cancellationToken)
    {
        var result = await links.ListAsync(cancellationToken);

        return result.Select(ShareMapper.ToDto).ToArray();
    }
}

public sealed class DeleteShareLinkCommandHandler(IShareLinkRepository links) : IRequestHandler<DeleteShareLinkCommand>
{
    public Task Handle(DeleteShareLinkCommand request, CancellationToken cancellationToken) =>
        links.DeleteAsync(ObjectId.Parse(request.Id), cancellationToken);
}
```

- [ ] **Step 4: 實作公開查詢**

`src/MyCollection.Application/Sharing/GetPublicShareQuery.cs`：

```csharp
using MediatR;
using MyCollection.Application.Auth;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Sharing;

public record GetPublicShareQuery(string Slug) : IRequest<PublicShareDto>;

/// <summary>
/// 匿名唯讀路徑。不注入 IUserContext，不碰 IItemRepository——
/// 全部走 IPublicCatalogReader 的白名單投影。
/// </summary>
public sealed class GetPublicShareQueryHandler(
    IShareLinkRepository links,
    IPublicCatalogReader catalog,
    IUserRepository users,
    TimeProvider timeProvider) : IRequestHandler<GetPublicShareQuery, PublicShareDto>
{
    public async Task<PublicShareDto> Handle(GetPublicShareQuery request, CancellationToken cancellationToken)
    {
        var link = await links.GetBySlugAsync(request.Slug, cancellationToken)
                   ?? throw new NotFoundException(nameof(ShareLink), request.Slug);

        // 過期連結對外表現得像不存在，不透露曾經存在過
        if (link.ExpiresAt is { } expiresAt && expiresAt <= timeProvider.GetUtcNow().UtcDateTime)
        {
            throw new NotFoundException(nameof(ShareLink), request.Slug);
        }

        var owner = await users.GetByIdAsync(link.OwnerId, cancellationToken);

        var categoryNames = await catalog.ListCategoryNamesAsync(link.OwnerId, cancellationToken);

        var items = await catalog.ListItemsAsync(
            link.OwnerId, link.Scope, link.IncludeCategoryIds, link.IncludePrice, cancellationToken);

        return new PublicShareDto(
            owner?.DisplayName ?? "Collector",
            link.Scope.ToString(),
            items.Select(i => new PublicItemDto(
                i.Id.ToString(),
                i.Name,
                i.Description,
                categoryNames.TryGetValue(i.CategoryId, out var name) ? name : string.Empty,
                i.Tags,
                i.Images
                    .OrderBy(img => img.Order)
                    .Select(img => new PublicImageDto(img.CardPath, img.ThumbPath, img.IsPrimary, img.Order))
                    .ToArray(),
                BsonJson.ToDictionary(i.Attributes),
                i.Price is null ? null : new PublicPriceDto(i.Price.Amount, i.Price.Currency))).ToArray());
    }
}
```

- [ ] **Step 5: 實作端點**

`src/MyCollection.Api/Endpoints/ShareEndpoints.cs`：

```csharp
using MediatR;
using MyCollection.Application.Sharing;

namespace MyCollection.Api.Endpoints;

public static class ShareEndpoints
{
    public static IEndpointRouteBuilder MapShareEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/shares").WithTags("Sharing").RequireAuthorization();

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListShareLinksQuery(), ct)));

        group.MapPost("/", async (CreateShareLinkCommand command, ISender sender, CancellationToken ct) =>
        {
            var created = await sender.Send(command, ct);
            return Results.Created($"/public/{created.Slug}", created);
        });

        group.MapDelete("/{id}", async (string id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteShareLinkCommand(id), ct);
            return Results.NoContent();
        });

        app.MapGet("/public/{slug}", async (string slug, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetPublicShareQuery(slug), ct)))
            .AllowAnonymous()
            .WithTags("Sharing");

        return app;
    }
}
```

`src/MyCollection.Api/Program.cs` 追加 `app.MapShareEndpoints();`。

- [ ] **Step 6: 跑單元測試確認通過**

Run: `dotnet test --filter ShareCommandTests`
Expected: `Passed: 6`

- [ ] **Step 7: 寫端到端測試**

`tests/MyCollection.Tests/Integration/ShareEndpointsTests.cs`：

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MyCollection.Application.Categories;
using MyCollection.Application.Items;
using MyCollection.Application.Sharing;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class ShareEndpointsTests(MongoFixture mongo) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await mongo.ResetAsync();
        _factory = new ApiFactory(mongo);
        _client = await AuthenticatedClient.CreateAsync(_factory, "sharer@example.com");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<string> SeedShowcasedItemAsync()
    {
        var category = (await (await _client.PostAsJsonAsync("/categories", new
        {
            name = "公仔", icon = "figure", kind = "Physical", fields = Array.Empty<object>()
        })).Content.ReadFromJsonAsync<CategoryDto>())!;

        await _client.PostAsJsonAsync("/items", new
        {
            categoryId = category.Id, name = "精選公仔", description = "描述",
            tags = new[] { "GSC" }, isShowcased = true, attributes = new { },
            acquisition = new { acquiredAt = "2026-01-01T00:00:00Z", amount = 12800, currency = "TWD", vendor = "GSC 官網" }
        });

        await _client.PostAsJsonAsync("/items", new
        {
            categoryId = category.Id, name = "非精選公仔", description = (string?)null,
            tags = Array.Empty<string>(), isShowcased = false, attributes = new { }, acquisition = (object?)null
        });

        return category.Id;
    }

    private async Task<ShareLinkDto> CreateShareAsync(bool includePrice = false)
    {
        var response = await _client.PostAsJsonAsync("/shares", new
        {
            scope = "Showcase", includeCategoryIds = Array.Empty<string>(), includePrice, expiresAt = (DateTime?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ShareLinkDto>())!;
    }

    [Fact]
    public async Task Public_page_is_anonymous_and_shows_only_showcased_items()
    {
        await SeedShowcasedItemAsync();
        var share = await CreateShareAsync();

        using var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync($"/public/{share.Slug}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = (await response.Content.ReadFromJsonAsync<PublicShareDto>())!;
        payload.Items.Should().ContainSingle().Which.Name.Should().Be("精選公仔");
        payload.OwnerDisplayName.Should().Be("Tester");
    }

    [Fact]
    public async Task Public_payload_never_contains_acquisition_by_default()
    {
        await SeedShowcasedItemAsync();
        var share = await CreateShareAsync(includePrice: false);

        using var anonymous = _factory.CreateClient();
        var raw = await anonymous.GetStringAsync($"/public/{share.Slug}");

        raw.Should().NotContain("acquisition");
        raw.Should().NotContain("12800");
        raw.Should().NotContain("GSC 官網");
        raw.Should().NotContain("acquiredAt");
    }

    [Fact]
    public async Task Public_payload_contains_price_only_when_share_opts_in()
    {
        await SeedShowcasedItemAsync();
        var share = await CreateShareAsync(includePrice: true);

        using var anonymous = _factory.CreateClient();
        var raw = await anonymous.GetStringAsync($"/public/{share.Slug}");

        raw.Should().Contain("12800");
        raw.Should().NotContain("GSC 官網", "vendor 永遠不外流");
        raw.Should().NotContain("acquiredAt", "購入日期永遠不外流");
    }

    [Fact]
    public async Task Unknown_slug_returns_404()
    {
        using var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync("/public/doesnotexist")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Expired_share_returns_404()
    {
        await SeedShowcasedItemAsync();
        var response = await _client.PostAsJsonAsync("/shares", new
        {
            scope = "Showcase",
            includeCategoryIds = Array.Empty<string>(),
            includePrice = false,
            expiresAt = DateTime.UtcNow.AddDays(-1)
        });
        var share = (await response.Content.ReadFromJsonAsync<ShareLinkDto>())!;

        using var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync($"/public/{share.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleted_share_stops_resolving()
    {
        await SeedShowcasedItemAsync();
        var share = await CreateShareAsync();

        (await _client.DeleteAsync($"/shares/{share.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync($"/public/{share.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Showcase_endpoint_returns_only_showcased_items()
    {
        await SeedShowcasedItemAsync();

        var result = await _client.GetFromJsonAsync<JsonElement>("/showcase");

        result.GetProperty("total").GetInt64().Should().Be(1);
        result.GetProperty("items")[0].GetProperty("name").GetString().Should().Be("精選公仔");
    }
}
```

- [ ] **Step 8: 跑測試確認通過**

Run: `dotnet test --filter ShareEndpointsTests`
Expected: `Passed: 7`

- [ ] **Step 9: 跑全部測試**

Run: `dotnet test`
Expected: `Failed: 0`

- [ ] **Step 10: Commit**

```bash
git add src tests
git commit -m "feat(sharing): 新增分享連結與匿名公開頁端點"
```

---

## 驗收

- [ ] `dotnet test` 全綠
- [ ] 上傳一張圖 → `/media/{thumbPath}` 回 WebP，品項第一張自動成為主圖
- [ ] 刪除圖片後三個檔案都不存在
- [ ] `/showcase` 只回 `isShowcased: true` 的品項
- [ ] 無痕視窗開 `/public/{slug}` 可讀，回應 payload 全文搜尋不到 `acquisition`、`acquiredAt`、`vendor`
- [ ] `includePrice: true` 時只多出 `price`，`vendor` 與 `acquiredAt` 仍不外流

**下一步：** `docs/superpowers/plans/2026-07-25-04-ingestion.md`
