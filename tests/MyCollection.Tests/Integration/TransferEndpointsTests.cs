using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Application.Sharing;
using MyCollection.Application.Transfer;
using MyCollection.Tests.Fixtures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class TransferEndpointsTests(MongoFixture mongo) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await mongo.ResetAsync();
        _factory = new ApiFactory(mongo);
        _client = await AuthenticatedClient.CreateAsync(_factory, "transfer@example.com");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static MultipartFormDataContent PngUpload()
    {
        using var image = new Image<Rgba32>(800, 600);
        var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder());

        var content = new ByteArrayContent(buffer.ToArray());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");

        return new MultipartFormDataContent { { content, "file", "test.png" } };
    }

    private async Task<CategoryDto> CreateCategoryAsync(string name = "黑膠唱片") =>
        (await (await _client.PostAsJsonAsync("/categories", new
        {
            name,
            icon = "disc-3",
            kind = "Physical",
            fields = new[]
            {
                new { key = "label", label = "廠牌", type = "Text", required = false, searchable = true, showOnCard = true }
            }
        })).Content.ReadFromJsonAsync<CategoryDto>())!;

    private async Task<ItemDto> CreateItemAsync(string categoryId, string name = "Kind of Blue") =>
        (await (await _client.PostAsJsonAsync("/items", new
        {
            categoryId,
            name,
            description = (string?)null,
            tags = new[] { "jazz" },
            isShowcased = true,
            attributes = new { label = "Columbia" }
        })).Content.ReadFromJsonAsync<ItemDto>())!;

    private async Task<ShareLinkDto> CreateShareLinkAsync() =>
        (await (await _client.PostAsJsonAsync("/shares", new
        {
            scope = "Showcase", includeCategoryIds = Array.Empty<string>(), includePrice = false, expiresAt = (DateTime?)null
        })).Content.ReadFromJsonAsync<ShareLinkDto>())!;

    private static async Task<ZipArchive> ReadArchiveAsync(HttpResponseMessage response)
    {
        var buffer = new MemoryStream(await response.Content.ReadAsByteArrayAsync());

        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    private static MultipartFormDataContent ArchiveUpload(byte[] zip)
    {
        var content = new ByteArrayContent(zip);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");

        return new MultipartFormDataContent { { content, "file", "archive.zip" } };
    }

    private async Task<byte[]> ExportBytesAsync() =>
        await (await _client.GetAsync("/export")).Content.ReadAsByteArrayAsync();

    [Fact]
    public async Task Export_requires_authentication()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync("/export");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Export_returns_a_zip_containing_manifest_and_images()
    {
        var category = await CreateCategoryAsync();
        var item = await CreateItemAsync(category.Id);
        (await _client.PostAsync($"/items/{item.Id}/images", PngUpload())).EnsureSuccessStatusCode();
        var share = await CreateShareLinkAsync();

        var response = await _client.GetAsync("/export");
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentDisposition!.FileName.Should().EndWith(".zip");

        using var archive = await ReadArchiveAsync(response);

        archive.GetEntry(ArchiveManifest.FileName).Should().NotBeNull();
        archive.Entries.Should().Contain(e => e.FullName.StartsWith("media/") && e.FullName.EndsWith(".webp"));

        await using var manifestStream = archive.GetEntry(ArchiveManifest.FileName)!.Open();
        using var copy = new MemoryStream();
        await manifestStream.CopyToAsync(copy);
        copy.Position = 0;

        var manifest = ArchiveManifestSerializer.Read(copy);
        manifest.Categories.Should().ContainSingle(c => c.Name == "黑膠唱片");
        manifest.Items.Should().ContainSingle(i => i.Name == "Kind of Blue");
        manifest.Items[0].Images.Should().ContainSingle();
        manifest.Items[0].Attributes["label"].AsString.Should().Be("Columbia");
        manifest.ShareLinks.Should().ContainSingle(s => s.Slug == share.Slug);
    }

    [Fact]
    public async Task Export_excludes_other_users_data()
    {
        var category = await CreateCategoryAsync();
        await CreateItemAsync(category.Id);
        await CreateShareLinkAsync();

        using var stranger = await AuthenticatedClient.CreateAsync(_factory, "stranger@example.com");
        var response = await stranger.GetAsync("/export");
        response.EnsureSuccessStatusCode();

        using var archive = await ReadArchiveAsync(response);
        await using var manifestStream = archive.GetEntry(ArchiveManifest.FileName)!.Open();
        using var copy = new MemoryStream();
        await manifestStream.CopyToAsync(copy);
        copy.Position = 0;

        var manifest = ArchiveManifestSerializer.Read(copy);
        manifest.Categories.Should().BeEmpty();
        manifest.Items.Should().BeEmpty();
        manifest.ShareLinks.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_requires_authentication()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsync("/import", ArchiveUpload([1, 2, 3]));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Import_rejects_a_file_that_is_not_a_zip()
    {
        var response = await _client.PostAsync("/import", ArchiveUpload([1, 2, 3, 4]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Import_rejects_an_unknown_schema_version_without_touching_data()
    {
        var category = await CreateCategoryAsync();
        await CreateItemAsync(category.Id);

        var tampered = new MemoryStream();
        using (var archive = new ZipArchive(tampered, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using var entry = archive.CreateEntry(ArchiveManifest.FileName).Open();
            // ExportedAt 必須是 Kind=Utc：UtcOnlyDateTimeSerializer 會擋下 default(DateTime)，
            // 那會在測試自己寫檔時就爆掉，根本走不到要驗證的匯入路徑。
            await ArchiveManifestSerializer.WriteAsync(
                entry,
                new ArchiveManifest { SchemaVersion = 99, ExportedAt = DateTime.UtcNow },
                default);
        }

        var response = await _client.PostAsync("/import", ArchiveUpload(tampered.ToArray()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 資料未被動過
        var items = await _client.GetFromJsonAsync<PagedResult<ItemDto>>("/items");
        items!.Total.Should().Be(1);
    }
}
