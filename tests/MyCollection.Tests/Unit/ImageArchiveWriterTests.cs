using System.IO.Compression;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Application.Transfer;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class ImageArchiveWriterTests
{
    private readonly Mock<IImageArchiveRepository> _repository = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly Mock<IUserContext> _userContext = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 28, 3, 0, 0, TimeSpan.Zero));

    private static readonly ObjectId OwnerId = ObjectId.GenerateNewId();
    private static readonly ObjectId ItemId = ObjectId.GenerateNewId();

    private static string Full => $"{OwnerId}/{ItemId}/img1-full.webp";
    private static string Card => $"{OwnerId}/{ItemId}/img1-card.webp";
    private static string Thumb => $"{OwnerId}/{ItemId}/img1-thumb.webp";

    public ImageArchiveWriterTests()
    {
        _userContext.SetupGet(u => u.UserId).Returns(OwnerId);

        _repository.Setup(r => r.ListItemsWithImagesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Item
            {
                Id = ItemId,
                OwnerId = OwnerId,
                CategoryId = ObjectId.GenerateNewId(),
                Name = "Kind of Blue",
                Images =
                [
                    new ItemImage
                    {
                        Id = "img1",
                        Path = Full,
                        CardPath = Card,
                        ThumbPath = Thumb,
                        IsPrimary = true,
                        Order = 0
                    }
                ],
                CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
            }]);

        _storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream([9, 9, 9]));
    }

    private ImageArchiveWriter CreateSut() => new(_repository.Object, _storage.Object, _userContext.Object, _time);

    private async Task<ZipArchive> WriteAsync()
    {
        var buffer = new MemoryStream();
        await CreateSut().WriteAsync(buffer, CancellationToken.None);
        buffer.Position = 0;

        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    private static ImageArchiveManifest ReadManifest(ZipArchive archive)
    {
        using var stream = archive.GetEntry(ImageArchiveManifest.FileName)!.Open();

        return ImageArchiveManifestSerializer.Read(stream);
    }

    [Fact]
    public async Task Packs_all_three_sizes_under_their_storage_paths()
    {
        using var archive = await WriteAsync();

        archive.Entries.Select(e => e.FullName).Should().BeEquivalentTo(
            Full, Card, Thumb, ImageArchiveManifest.FileName);
    }

    /// <summary>
    /// manifest 帶的 fileCount 與 missing 只有在所有圖檔都處理完之後才知道，
    /// 所以它必須是最後一個 entry。若有人把它移回開頭，這個測試會失敗。
    /// </summary>
    [Fact]
    public async Task Writes_the_manifest_last_so_it_can_report_what_actually_went_in()
    {
        using var archive = await WriteAsync();

        archive.Entries[^1].FullName.Should().Be(ImageArchiveManifest.FileName);
        ReadManifest(archive).FileCount.Should().Be(3);
    }

    [Fact]
    public async Task Manifest_records_the_owner_so_the_import_side_can_reject_a_foreign_archive()
    {
        using var archive = await WriteAsync();

        var manifest = ReadManifest(archive);

        manifest.SchemaVersion.Should().Be(ImageArchiveManifest.CurrentSchemaVersion);
        manifest.OwnerId.Should().Be(OwnerId.ToString());
        manifest.ExportedAt.Should().Be(new DateTime(2026, 7, 28, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Missing_file_is_reported_in_the_manifest_and_excluded_from_the_count()
    {
        _storage.Setup(s => s.OpenReadAsync(Card, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream?)null);

        using var archive = await WriteAsync();

        archive.Entries.Select(e => e.FullName).Should().NotContain(Card);

        var manifest = ReadManifest(archive);

        manifest.FileCount.Should().Be(2);
        manifest.Missing.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { ItemName = "Kind of Blue", Path = Card });
    }

    [Fact]
    public async Task Items_without_images_never_reach_the_writer()
    {
        _repository.Setup(r => r.ListItemsWithImagesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        using var archive = await WriteAsync();

        archive.Entries.Select(e => e.FullName).Should().Equal(ImageArchiveManifest.FileName);
        ReadManifest(archive).FileCount.Should().Be(0);
    }

    /// <summary>
    /// 模擬 Kestrel 的 HttpResponse.Body：AllowSynchronousIO 預設為 false，
    /// 任何同步寫入都會擲例外。MemoryStream 無條件容忍同步 I/O，
    /// 所以少了這個 stub，「匯出端點會炸掉」這件事在測試裡是看不見的。
    /// </summary>
    private sealed class SyncRejectingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() =>
            throw new InvalidOperationException("Synchronous operations are disallowed.");

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new InvalidOperationException("Synchronous operations are disallowed.");

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new InvalidOperationException("Synchronous operations are disallowed.");

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.WriteAsync(buffer, offset, count, cancellationToken);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);
    }

    [Fact]
    public async Task Writes_through_a_stream_that_rejects_synchronous_io_without_throwing()
    {
        var inner = new MemoryStream();

        await CreateSut().WriteAsync(new SyncRejectingStream(inner), CancellationToken.None);

        using var buffer = new MemoryStream(inner.ToArray());
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);

        archive.Entries.Select(e => e.FullName).Should().BeEquivalentTo(
            Full, Card, Thumb, ImageArchiveManifest.FileName);
    }
}
