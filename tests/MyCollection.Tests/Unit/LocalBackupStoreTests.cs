using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MyCollection.Infrastructure.Storage;

namespace MyCollection.Tests.Unit;

public class LocalBackupStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mc-backup-{Guid.NewGuid():N}");
    private readonly LocalBackupStore _sut;
    private static readonly ObjectId OwnerId = ObjectId.GenerateNewId();

    public LocalBackupStoreTests() =>
        _sut = new LocalBackupStore(Options.Create(new StorageOptions { BackupRoot = _root }));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private async Task WriteAsync(string fileName)
    {
        await using var stream = await _sut.CreateAsync(OwnerId, fileName, CancellationToken.None);
        await stream.WriteAsync(new byte[] { 1, 2, 3 });
    }

    private string[] Files() =>
        Directory.Exists(Path.Combine(_root, OwnerId.ToString()))
            ? [.. Directory.GetFiles(Path.Combine(_root, OwnerId.ToString())).Select(Path.GetFileName)!]
            : [];

    [Fact]
    public async Task Create_writes_the_file_under_the_owner_folder()
    {
        await WriteAsync("pre-import-20260728-030000.zip");

        Files().Should().Equal("pre-import-20260728-030000.zip");
    }

    [Fact]
    public async Task Prune_keeps_only_the_newest_files_for_that_owner()
    {
        await WriteAsync("pre-import-20260701-000000.zip");
        await WriteAsync("pre-import-20260702-000000.zip");
        await WriteAsync("pre-import-20260703-000000.zip");
        await WriteAsync("pre-import-20260704-000000.zip");

        await _sut.PruneAsync(OwnerId, keep: 3, CancellationToken.None);

        Files().Should().BeEquivalentTo(
            "pre-import-20260702-000000.zip",
            "pre-import-20260703-000000.zip",
            "pre-import-20260704-000000.zip");
    }

    [Fact]
    public async Task Prune_does_not_touch_another_owners_backups()
    {
        var other = ObjectId.GenerateNewId();
        await using (var stream = await _sut.CreateAsync(other, "pre-import-20260101-000000.zip", CancellationToken.None))
        {
            await stream.WriteAsync(new byte[] { 1 });
        }

        await WriteAsync("pre-import-20260704-000000.zip");
        await _sut.PruneAsync(OwnerId, keep: 1, CancellationToken.None);

        Directory.GetFiles(Path.Combine(_root, other.ToString())).Should().ContainSingle();
    }

    [Fact]
    public async Task Prune_is_silent_when_the_owner_has_no_backups()
    {
        var act = async () => await _sut.PruneAsync(ObjectId.GenerateNewId(), keep: 3, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
