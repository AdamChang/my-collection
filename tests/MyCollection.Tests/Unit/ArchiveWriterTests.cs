using System.IO.Compression;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Application.Transfer;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class ArchiveWriterTests
{
    private readonly Mock<ITransferRepository> _repository = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 28, 3, 0, 0, TimeSpan.Zero));

    private static readonly ObjectId OwnerId = ObjectId.GenerateNewId();
    private static readonly ObjectId CategoryId = ObjectId.GenerateNewId();
    private static readonly ObjectId ItemId = ObjectId.GenerateNewId();

    public ArchiveWriterTests()
    {
        _repository.Setup(r => r.ListOwnCategoriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Category
            {
                Id = CategoryId,
                OwnerId = OwnerId,
                Name = "黑膠唱片",
                Kind = CategoryKind.Physical,
                CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
            }]);

        _repository.Setup(r => r.ListExportableItemsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Item
            {
                Id = ItemId,
                OwnerId = OwnerId,
                CategoryId = CategoryId,
                Name = "Kind of Blue",
                Images =
                [
                    new ItemImage
                    {
                        Id = "img1",
                        Path = $"{OwnerId}/{ItemId}/img1-full.webp",
                        CardPath = $"{OwnerId}/{ItemId}/img1-card.webp",
                        ThumbPath = $"{OwnerId}/{ItemId}/img1-thumb.webp",
                        IsPrimary = true,
                        Order = 0
                    }
                ],
                CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
            }]);

        _repository.Setup(r => r.ListOwnShareLinksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream([9, 9, 9]));
    }

    private ArchiveWriter CreateSut() => new(_repository.Object, _storage.Object, _time);

    private async Task<ZipArchive> WriteAsync()
    {
        var buffer = new MemoryStream();
        await CreateSut().WriteAsync(buffer, CancellationToken.None);
        buffer.Position = 0;

        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    [Fact]
    public async Task Writes_manifest_and_only_the_full_size_image()
    {
        using var archive = await WriteAsync();

        archive.Entries.Select(e => e.FullName).Should().BeEquivalentTo(
            ArchiveManifest.FileName,
            ArchivePaths.Image(ItemId, "img1"));
    }

    [Fact]
    public async Task Reads_the_full_size_path_from_storage_not_card_or_thumb()
    {
        using var archive = await WriteAsync();

        _storage.Verify(
            s => s.OpenReadAsync($"{OwnerId}/{ItemId}/img1-full.webp", It.IsAny<CancellationToken>()),
            Times.Once);
        _storage.Verify(
            s => s.OpenReadAsync(It.Is<string>(p => p.Contains("-card") || p.Contains("-thumb")),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Manifest_omits_owner_id_and_uses_archive_relative_image_paths()
    {
        using var archive = await WriteAsync();

        await using var manifestStream = archive.GetEntry(ArchiveManifest.FileName)!.Open();
        using var copy = new MemoryStream();
        await manifestStream.CopyToAsync(copy);
        copy.Position = 0;

        var json = System.Text.Encoding.UTF8.GetString(copy.ToArray());
        json.Should().NotContain(OwnerId.ToString());

        copy.Position = 0;
        var manifest = ArchiveManifestSerializer.Read(copy);

        manifest.SchemaVersion.Should().Be(ArchiveManifest.CurrentSchemaVersion);
        manifest.ExportedAt.Should().Be(new DateTime(2026, 7, 28, 3, 0, 0, DateTimeKind.Utc));
        manifest.Items[0].Images[0].File.Should().Be(ArchivePaths.Image(ItemId, "img1"));
        manifest.Items[0].Images[0].IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task Missing_image_file_is_still_listed_in_manifest_and_simply_absent_from_the_zip()
    {
        _storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream?)null);

        using var archive = await WriteAsync();

        archive.Entries.Select(e => e.FullName).Should().Equal(ArchiveManifest.FileName);

        await using var manifestStream = archive.GetEntry(ArchiveManifest.FileName)!.Open();
        using var copy = new MemoryStream();
        await manifestStream.CopyToAsync(copy);
        copy.Position = 0;

        var manifest = ArchiveManifestSerializer.Read(copy);
        manifest.Items[0].Images.Should().ContainSingle(i => i.Id == "img1");
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
        var sut = CreateSut();

        await sut.WriteAsync(new SyncRejectingStream(inner), CancellationToken.None);

        using var buffer = new MemoryStream(inner.ToArray());
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);

        archive.Entries.Select(e => e.FullName).Should().BeEquivalentTo(
            ArchiveManifest.FileName,
            ArchivePaths.Image(ItemId, "img1"));
    }
}
