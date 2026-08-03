using System.IO.Compression;
using System.Text;
using FluentAssertions;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Application.Transfer;

namespace MyCollection.Tests.Unit;

public class ImportImageArchiveCommandTests
{
    private static readonly ObjectId OwnerId = ObjectId.GenerateNewId();
    private static readonly ObjectId OtherOwnerId = ObjectId.GenerateNewId();
    private static readonly ObjectId ItemId = ObjectId.GenerateNewId();

    private static string Full => $"{OwnerId}/{ItemId}/img1-full.webp";
    private static string Card => $"{OwnerId}/{ItemId}/img1-card.webp";

    private readonly FakeFileStorage _storage = new();
    private readonly Mock<IUserContext> _userContext = new();

    public ImportImageArchiveCommandTests() =>
        _userContext.SetupGet(u => u.UserId).Returns(OwnerId);

    private ImportImageArchiveCommandHandler CreateSut() => new(_storage, _userContext.Object);

    private Task<ImageImportResultDto> ImportAsync(byte[] zip) =>
        CreateSut().Handle(new ImportImageArchiveCommand(new MemoryStream(zip)), CancellationToken.None);

    private static ImageArchiveManifest Manifest(string ownerId, params MissingImageFile[] missing) => new()
    {
        ExportedAt = new DateTime(2026, 7, 28, 3, 0, 0, DateTimeKind.Utc),
        OwnerId = ownerId,
        Missing = [.. missing]
    };

    private static byte[] BuildArchive(ImageArchiveManifest manifest, params string[] paths) =>
        BuildArchive(SerializeManifest(manifest), paths);

    private static byte[] BuildArchive(string? manifestJson, params string[] paths)
    {
        var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var path in paths)
            {
                using var entry = archive.CreateEntry(path).Open();
                entry.Write(Encoding.UTF8.GetBytes(path));
            }

            if (manifestJson is not null)
            {
                using var entry = archive.CreateEntry(ImageArchiveManifest.FileName).Open();
                entry.Write(Encoding.UTF8.GetBytes(manifestJson));
            }
        }

        return buffer.ToArray();
    }

    private static string SerializeManifest(ImageArchiveManifest manifest)
    {
        var buffer = new MemoryStream();
        ImageArchiveManifestSerializer.WriteAsync(buffer, manifest, CancellationToken.None).GetAwaiter().GetResult();

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    [Fact]
    public async Task Writes_the_files_that_are_not_on_this_machine_yet()
    {
        var result = await ImportAsync(BuildArchive(Manifest(OwnerId.ToString()), Full, Card));

        result.Written.Should().Be(2);
        result.Skipped.Should().Be(0);
        _storage.Files.Keys.Should().BeEquivalentTo(Full, Card);
    }

    /// <summary>
    /// 路徑含 imageId，而 imageId 每次上傳都是新的 ObjectId——同一個路徑不可能
    /// 合法地裝著不同內容。所以既有檔案一律略過，而且必須是「不動它」，
    /// 不是「重寫一份一樣的」。
    /// </summary>
    [Fact]
    public async Task Leaves_an_existing_file_untouched()
    {
        _storage.Files[Full] = [1, 2, 3];

        var result = await ImportAsync(BuildArchive(Manifest(OwnerId.ToString()), Full, Card));

        result.Written.Should().Be(1);
        result.Skipped.Should().Be(1);
        _storage.Files[Full].Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Rejects_an_archive_exported_by_another_account()
    {
        var zip = BuildArchive(Manifest(OtherOwnerId.ToString()), $"{OtherOwnerId}/{ItemId}/img1-full.webp");

        var act = () => ImportAsync(zip);

        await act.Should().ThrowAsync<InvalidArchiveException>().WithMessage("*其他帳號*");
        _storage.Files.Should().BeEmpty();
    }

    /// <summary>
    /// IFileStorage 只保證不寫出 storage root 之外，不保證不寫進別人的目錄。
    /// 前綴檢查必須在任何寫入之前跑完整包，否則前面那些合法的 entry 已經落地了。
    /// </summary>
    [Theory]
    [InlineData("{0}/../../escaped.webp")]
    [InlineData("someone-else/{1}/img1-full.webp")]
    public async Task Rejects_a_foreign_path_before_writing_anything(string template)
    {
        var hostile = string.Format(template, OwnerId, ItemId);
        var zip = BuildArchive(Manifest(OwnerId.ToString()), Full, hostile);

        var act = () => ImportAsync(zip);

        await act.Should().ThrowAsync<InvalidArchiveException>();
        _storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_an_entry_that_is_not_a_webp()
    {
        var zip = BuildArchive(Manifest(OwnerId.ToString()), $"{OwnerId}/{ItemId}/notes.txt");

        var act = () => ImportAsync(zip);

        await act.Should().ThrowAsync<InvalidArchiveException>();
        _storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_the_old_collection_data_archive_with_a_message_that_says_why()
    {
        var legacy = """{ "schemaVersion": 1, "exportedAt": "2026-07-01T00:00:00Z", "categories": [], "items": [] }""";

        var act = () => ImportAsync(BuildArchive(legacy));

        await act.Should().ThrowAsync<InvalidArchiveException>().WithMessage("*舊版*");
    }

    [Fact]
    public async Task Rejects_an_archive_without_a_manifest()
    {
        var act = () => ImportAsync(BuildArchive(manifestJson: null, Full));

        await act.Should().ThrowAsync<InvalidArchiveException>()
            .WithMessage($"*{ImageArchiveManifest.FileName}*");
        _storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_a_manifest_without_an_owner()
    {
        var act = () => ImportAsync(BuildArchive($$"""{ "schemaVersion": {{ImageArchiveManifest.CurrentSchemaVersion}} }"""));

        await act.Should().ThrowAsync<InvalidArchiveException>();
    }

    [Fact]
    public async Task Rejects_a_file_that_is_not_a_zip()
    {
        var act = () => ImportAsync(Encoding.UTF8.GetBytes("this is not a zip"));

        await act.Should().ThrowAsync<InvalidArchiveException>().WithMessage("*ZIP*");
    }

    /// <summary>
    /// 缺檔是匯出端當下的事實，匯入端補不回來，但不能吞掉——使用者需要知道
    /// 這張圖在來源機器上就已經不見了，而不是等到某天看到破圖。
    /// </summary>
    [Fact]
    public async Task Surfaces_the_files_that_were_already_missing_at_export_time()
    {
        var manifest = Manifest(
            OwnerId.ToString(),
            new MissingImageFile { ItemName = "Kind of Blue", Path = Card });

        var result = await ImportAsync(BuildArchive(manifest, Full));

        result.Warnings.Should().ContainSingle()
            .Which.Should().Contain("Kind of Blue").And.Contain(Card);
    }

    [Fact]
    public async Task Summarises_the_tail_when_a_lot_of_files_were_missing()
    {
        var missing = Enumerable.Range(0, 25)
            .Select(i => new MissingImageFile { ItemName = $"Item {i}", Path = $"{OwnerId}/{ItemId}/img{i}-full.webp" })
            .ToArray();

        var result = await ImportAsync(BuildArchive(Manifest(OwnerId.ToString(), missing), Full));

        result.Warnings.Should().HaveCount(21);
        result.Warnings[^1].Should().Contain("另有 5");
    }

    /// <summary>
    /// 匯入是非破壞性的：只新增檔案，永不刪除。刪除方法一旦被呼叫就讓測試炸掉，
    /// 這個性質才不會在日後被無聲地改掉。
    /// </summary>
    private sealed class FakeFileStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

        public async Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            Files[relativePath] = buffer.ToArray();

            return relativePath;
        }

        public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct) =>
            Task.FromResult<Stream?>(
                Files.TryGetValue(relativePath, out var content) ? new MemoryStream(content) : null);

        public Task DeleteAsync(string relativePath, CancellationToken ct) =>
            throw new InvalidOperationException("匯入不應該刪除任何檔案。");

        public Task DeleteDirectoryAsync(string relativePrefix, CancellationToken ct) =>
            throw new InvalidOperationException("匯入不應該刪除任何目錄。");
    }
}
