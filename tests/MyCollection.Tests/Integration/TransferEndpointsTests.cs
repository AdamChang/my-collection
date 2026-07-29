using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MyCollection.Application.Categories;
using MyCollection.Application.Items;
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

    private static async Task<ZipArchive> ReadArchiveAsync(HttpResponseMessage response)
    {
        var buffer = new MemoryStream(await response.Content.ReadAsByteArrayAsync());

        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

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
    }

    [Fact]
    public async Task Export_excludes_other_users_data()
    {
        var category = await CreateCategoryAsync();
        await CreateItemAsync(category.Id);

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
    }
}
