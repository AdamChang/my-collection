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

    [Fact]
    public async Task DeleteDirectory_removes_every_file_under_the_prefix()
    {
        var sut = CreateSut();
        await sut.SaveAsync("owner/item/a-full.webp", Content("x"), CancellationToken.None);
        await sut.SaveAsync("owner/item/b-thumb.webp", Content("x"), CancellationToken.None);

        await sut.DeleteDirectoryAsync("owner/item", CancellationToken.None);

        Directory.Exists(Path.Combine(_root, "owner", "item")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteDirectory_leaves_sibling_directories_untouched()
    {
        var sut = CreateSut();
        await sut.SaveAsync("owner/item-a/x-full.webp", Content("x"), CancellationToken.None);
        await sut.SaveAsync("owner/item-b/y-full.webp", Content("x"), CancellationToken.None);

        await sut.DeleteDirectoryAsync("owner/item-a", CancellationToken.None);

        File.Exists(Path.Combine(_root, "owner", "item-b", "y-full.webp")).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteDirectory_is_silent_when_the_directory_does_not_exist()
    {
        var sut = CreateSut();

        var act = async () => await sut.DeleteDirectoryAsync("owner/missing", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteDirectory_rejects_paths_that_escape_the_root()
    {
        var sut = CreateSut();

        var act = async () => await sut.DeleteDirectoryAsync("../../etc", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
