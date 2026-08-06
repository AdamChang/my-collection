using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using MyCollection.Application.Auth;
using MyCollection.Application.Categories;
using MyCollection.Application.Items;
using MyCollection.Application.Transfer;
using MyCollection.Tests.Fixtures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace MyCollection.Tests.Integration;

/// <summary>
/// 這組測試的核心情境是「兩台機器共用同一個 MongoDB，但各有各的本地圖片目錄」。
/// <see cref="ApiFactory"/> 每次建立都會分配一個新的 Storage:LocalRoot，所以第二個
/// factory 天然就是一台「資料齊全、圖片全缺」的機器——正是這個功能要解決的處境。
/// </summary>
[Collection(MongoCollection.Name)]
public class ImageTransferEndpointsTests(MongoFixture mongo) : IAsyncLifetime
{
    private const string Email = "image-transfer@example.com";
    private const string Password = "P@ssw0rd!";

    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await mongo.ResetAsync();
        _factory = new ApiFactory(mongo);
        _client = await AuthenticatedClient.CreateAsync(_factory, Email);
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
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        return new MultipartFormDataContent { { content, "file", "test.png" } };
    }

    private static MultipartFormDataContent ArchiveUpload(byte[] zip)
    {
        var content = new ByteArrayContent(zip);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

        return new MultipartFormDataContent { { content, "file", "images.zip" } };
    }

    private async Task<ItemDto> SeedItemWithImageAsync(HttpClient client)
    {
        var category = (await (await client.PostAsJsonAsync("/categories", new
        {
            name = "黑膠唱片",
            icon = "disc-3",
            kind = "Physical",
            defaultDisplayMode = "List",
            fields = Array.Empty<object>()
        })).Content.ReadFromJsonAsync<CategoryDto>())!;

        var item = (await (await client.PostAsJsonAsync("/items", new
        {
            categoryId = category.Id,
            name = "Kind of Blue",
            description = (string?)null,
            tags = Array.Empty<string>(),
            isShowcased = true,
            attributes = new { }
        })).Content.ReadFromJsonAsync<ItemDto>())!;

        (await client.PostAsync($"/items/{item.Id}/images", PngUpload())).EnsureSuccessStatusCode();

        return (await (await client.GetAsync($"/items/{item.Id}")).Content.ReadFromJsonAsync<ItemDto>())!;
    }

    private async Task<byte[]> ExportAsync(HttpClient client)
    {
        var response = await client.GetAsync("/images/export");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>同一個帳號登入到另一個 factory：資料庫共用，本地圖片目錄不共用。</summary>
    private async Task<HttpClient> SecondMachineAsync(ApiFactory factory)
    {
        var client = factory.CreateClient();

        var auth = await (await client.PostAsJsonAsync("/auth/login", new { email = Email, password = Password }))
            .Content.ReadFromJsonAsync<AuthResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        return client;
    }

    [Fact]
    public async Task Export_requires_authentication()
    {
        using var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync("/images/export")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Import_requires_authentication()
    {
        using var anonymous = _factory.CreateClient();

        (await anonymous.PostAsync("/images/import", ArchiveUpload([1, 2, 3])))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Export_packs_every_stored_size_under_the_path_the_database_records()
    {
        var item = await SeedItemWithImageAsync(_client);

        var response = await _client.GetAsync("/images/export");
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain("mycollection-images-");

        using var archive = new ZipArchive(
            new MemoryStream(await response.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);

        var image = item.Images.Single();

        archive.Entries.Select(e => e.FullName).Should().BeEquivalentTo(
            image.Path, image.CardPath, image.ThumbPath, ImageArchiveManifest.FileName);
    }

    [Fact]
    public async Task Importing_on_a_second_machine_restores_the_images_the_shared_database_already_knows_about()
    {
        var item = await SeedItemWithImageAsync(_client);
        var image = item.Images.Single();
        var zip = await ExportAsync(_client);

        await using var second = new ApiFactory(mongo);
        using var secondClient = await SecondMachineAsync(second);

        // 同一份 DB，所以品項與圖片路徑都在；缺的只有檔案本身。
        var beforeItem = await (await secondClient.GetAsync($"/items/{item.Id}")).Content.ReadFromJsonAsync<ItemDto>();
        beforeItem!.Images.Single().Path.Should().Be(image.Path);
        (await secondClient.GetAsync($"/media/{image.Path}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var result = await (await secondClient.PostAsync("/images/import", ArchiveUpload(zip)))
            .Content.ReadFromJsonAsync<ImageImportResultDto>();

        result!.Written.Should().Be(3);
        result.Skipped.Should().Be(0);
        result.Warnings.Should().BeEmpty();

        foreach (var path in new[] { image.Path, image.CardPath, image.ThumbPath })
        {
            (await secondClient.GetAsync($"/media/{path}")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Importing_the_same_archive_twice_writes_nothing_the_second_time()
    {
        await SeedItemWithImageAsync(_client);
        var zip = await ExportAsync(_client);

        await using var second = new ApiFactory(mongo);
        using var secondClient = await SecondMachineAsync(second);

        (await secondClient.PostAsync("/images/import", ArchiveUpload(zip))).EnsureSuccessStatusCode();

        var result = await (await secondClient.PostAsync("/images/import", ArchiveUpload(zip)))
            .Content.ReadFromJsonAsync<ImageImportResultDto>();

        result!.Written.Should().Be(0);
        result.Skipped.Should().Be(3);
    }

    [Fact]
    public async Task Import_rejects_an_archive_exported_by_another_account()
    {
        await SeedItemWithImageAsync(_client);
        var zip = await ExportAsync(_client);

        using var stranger = await AuthenticatedClient.CreateAsync(_factory, "stranger@example.com");

        var response = await stranger.PostAsync("/images/import", ArchiveUpload(zip));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Import_rejects_an_empty_file()
    {
        var response = await _client.PostAsync("/images/import", ArchiveUpload([]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
