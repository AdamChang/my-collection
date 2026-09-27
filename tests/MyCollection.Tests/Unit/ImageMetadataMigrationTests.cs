using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Auth;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Application.Media;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace MyCollection.Tests.Unit;

public class ImageMetadataMigrationTests
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IItemRepository> _items = new();
    private readonly InMemoryFileStorage _storage = new();
    private readonly List<ObjectId?> _searchedAs = [];

    public ImageMetadataMigrationTests()
    {
        _users.Setup(r => r.ListIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Owner]);
    }

    private ImageMetadataMigration CreateSut(params Item[] items)
    {
        var services = new ServiceCollection();
        services.AddScoped<BackgroundUserContext>();
        services.AddSingleton(_users.Object);
        services.AddScoped<IItemRepository>(sp =>
        {
            // 記下查詢當下的身分，驗證每位使用者的品項是在自己的 owner filter 下列出
            var context = sp.GetRequiredService<BackgroundUserContext>();
            _items.Setup(r => r.SearchAsync(It.IsAny<ItemQuerySpec>(), It.IsAny<CancellationToken>()))
                .Callback(() => _searchedAs.Add(context.UserId))
                .ReturnsAsync((ItemQuerySpec spec, CancellationToken _) => new PagedResult<Item>(
                    items.Skip((spec.Page - 1) * spec.PageSize).Take(spec.PageSize).ToList(),
                    items.Length, spec.Page, spec.PageSize));
            return _items.Object;
        });
        services.AddSingleton<IFileStorage>(_storage);
        services.AddSingleton<IImageProcessor, ImageSharpProcessor>();

        return new ImageMetadataMigration(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ImageMetadataMigration>.Instance);
    }

    private Item ItemWithImage(string imageId, byte[]? full)
    {
        var itemId = ObjectId.GenerateNewId();
        var prefix = $"{Owner}/{itemId}/{imageId}";
        var image = new ItemImage
        {
            Id = imageId,
            Path = $"{prefix}-full.webp",
            CardPath = $"{prefix}-card.webp",
            ThumbPath = $"{prefix}-thumb.webp"
        };

        if (full is not null)
        {
            _storage.Files[image.Path] = full;
            _storage.Files[image.CardPath] = full;
            _storage.Files[image.ThumbPath] = full;
        }

        return new Item
        {
            Id = itemId,
            OwnerId = Owner,
            CategoryId = ObjectId.GenerateNewId(),
            Name = imageId,
            Images = [image]
        };
    }

    /// <summary>模擬遷移前的舊輸出：WebP 仍帶 GPS 與未套用的 Orientation。</summary>
    private static byte[] LegacyWebp(bool withGps, ushort orientation = 1)
    {
        using var image = new Image<Rgba32>(300, 200);
        if (withGps || orientation != 1)
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, orientation);
            if (withGps)
            {
                exif.SetValue(ExifTag.GPSLatitude, [new Rational(25, 1), new Rational(2, 1), new Rational(0, 1)]);
            }

            image.Metadata.ExifProfile = exif;
        }

        using var buffer = new MemoryStream();
        image.Save(buffer, new WebpEncoder());
        return buffer.ToArray();
    }

    [Fact]
    public async Task Dry_run_reports_without_writing()
    {
        var sut = CreateSut(
            ItemWithImage("gps", LegacyWebp(withGps: true)),
            ItemWithImage("rotated", LegacyWebp(withGps: false, orientation: 6)),
            ItemWithImage("clean", LegacyWebp(withGps: false)));
        var before = _storage.Files.ToDictionary();

        var report = await sut.RunAsync(apply: false, CancellationToken.None);

        report.Should().Be(new ImageMetadataMigrationReport(Total: 3, Clean: 1, NeedsStripping: 2, WithGps: 1, Missing: 0, Failed: 0));
        _storage.Files.Should().BeEquivalentTo(before);
        _storage.SaveOrder.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_rewrites_all_sizes_in_place_without_metadata_and_full_last()
    {
        var item = ItemWithImage("gps", LegacyWebp(withGps: true, orientation: 6));
        var sut = CreateSut(item);

        var report = await sut.RunAsync(apply: true, CancellationToken.None);

        report.NeedsStripping.Should().Be(1);
        var image = item.Images[0];
        // full 最後寫：它是「已處理」的判斷依據，中途失敗重跑時才不會漏掉 card／thumb
        _storage.SaveOrder.Should().Equal(image.CardPath, image.ThumbPath, image.Path);
        foreach (var path in _storage.SaveOrder)
        {
            Image.Identify(_storage.Files[path]).Metadata.ExifProfile.Should().BeNull();
        }

        Image.Identify(_storage.Files[image.Path]).Size.Should().Be(new Size(200, 300));
    }

    [Fact]
    public async Task Rerun_after_apply_skips_already_clean_images()
    {
        var sut = CreateSut(ItemWithImage("gps", LegacyWebp(withGps: true)));
        await sut.RunAsync(apply: true, CancellationToken.None);
        _storage.SaveOrder.Clear();

        var report = await sut.RunAsync(apply: true, CancellationToken.None);

        report.Clean.Should().Be(1);
        _storage.SaveOrder.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_or_corrupt_files_are_counted_and_do_not_stop_the_run()
    {
        var corrupt = ItemWithImage("corrupt", "not an image"u8.ToArray());
        var sut = CreateSut(
            ItemWithImage("missing", full: null),
            corrupt,
            ItemWithImage("gps", LegacyWebp(withGps: true)));

        var report = await sut.RunAsync(apply: true, CancellationToken.None);

        report.Should().Be(new ImageMetadataMigrationReport(Total: 3, Clean: 0, NeedsStripping: 1, WithGps: 1, Missing: 1, Failed: 1));
        report.HasProblems.Should().BeTrue();
    }

    [Fact]
    public async Task Lists_items_page_by_page_under_each_owner_identity()
    {
        var items = Enumerable.Range(0, ImageMetadataMigration.PageSize + 1)
            .Select(i => ItemWithImage($"img{i}", LegacyWebp(withGps: false)))
            .ToArray();
        var sut = CreateSut(items);

        var report = await sut.RunAsync(apply: false, CancellationToken.None);

        report.Total.Should().Be(items.Length);
        _searchedAs.Should().HaveCount(2).And.OnlyContain(id => id == Owner);
    }

    private sealed class InMemoryFileStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
        public List<string> SaveOrder { get; } = [];
        private readonly HashSet<string> _openReaders = new(StringComparer.Ordinal);

        public async Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct)
        {
            // 模擬 LocalFileStorage 在 Windows 上的行為：讀取中的檔案不能覆寫
            if (_openReaders.Contains(relativePath))
            {
                throw new IOException($"'{relativePath}' is being used by another process.");
            }

            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            Files[relativePath] = buffer.ToArray();
            SaveOrder.Add(relativePath);
            return relativePath;
        }

        public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct)
        {
            if (!Files.TryGetValue(relativePath, out var bytes))
            {
                return Task.FromResult<Stream?>(null);
            }

            _openReaders.Add(relativePath);
            return Task.FromResult<Stream?>(new ReaderStream(bytes, () => _openReaders.Remove(relativePath)));
        }

        private sealed class ReaderStream(byte[] bytes, Action onDispose) : MemoryStream(bytes)
        {
            protected override void Dispose(bool disposing)
            {
                onDispose();
                base.Dispose(disposing);
            }
        }

        public Task DeleteAsync(string relativePath, CancellationToken ct) => Task.CompletedTask;

        public Task DeleteDirectoryAsync(string relativePrefix, CancellationToken ct) => Task.CompletedTask;
    }
}
