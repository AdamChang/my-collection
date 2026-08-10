using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Application.Sharing;
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

    private async Task<ItemDto> CreateItemAsync(bool isShowcased = false)
    {
        var category = (await (await _client.PostAsJsonAsync("/categories", new
        {
            name = "公仔", icon = "figure", kind = "Physical", defaultDisplayMode = "List",
            fields = Array.Empty<object>()
        })).Content.ReadFromJsonAsync<CategoryDto>())!;

        return (await (await _client.PostAsJsonAsync("/items", new
        {
            categoryId = category.Id, name = "公仔", description = (string?)null,
            tags = Array.Empty<string>(), isShowcased,
            attributes = new { }, acquisition = (object?)null
        })).Content.ReadFromJsonAsync<ItemDto>())!;
    }

    private async Task<ShareLinkDto> CreateShowcaseShareAsync(DateTime? expiresAt = null)
    {
        var response = await _client.PostAsJsonAsync("/shares", new
        {
            scope = "Showcase",
            includeCategoryIds = Array.Empty<string>(),
            includePrice = false,
            includeRating = false,
            collageSlotCount = 4,
            expiresAt
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ShareLinkDto>())!;
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

    [Fact]
    public async Task Media_endpoint_refuses_files_that_are_not_webp()
    {
        var storage = _factory.Services.GetRequiredService<IFileStorage>();
        await storage.SaveAsync("owner/secret.zip", new MemoryStream([1, 2, 3]), CancellationToken.None);

        var response = await _client.GetAsync("/media/owner/secret.zip");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Media_endpoint_requires_authentication()
    {
        var item = await CreateItemAsync();
        var image = (await (await _client.PostAsync($"/items/{item.Id}/images", PngUpload()))
            .Content.ReadFromJsonAsync<ItemImageDto>())!;

        using var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync($"/media/{image.CardPath}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Media_endpoint_does_not_serve_another_users_image()
    {
        var item = await CreateItemAsync();
        var image = (await (await _client.PostAsync($"/items/{item.Id}/images", PngUpload()))
            .Content.ReadFromJsonAsync<ItemImageDto>())!;
        using var intruder = await AuthenticatedClient.CreateAsync(_factory, "intruder-reader@example.com");

        var response = await intruder.GetAsync($"/media/{image.CardPath}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Public_media_endpoint_serves_only_images_in_the_share_scope()
    {
        var sharedItem = await CreateItemAsync(isShowcased: true);
        var sharedImage = (await (await _client.PostAsync($"/items/{sharedItem.Id}/images", PngUpload()))
            .Content.ReadFromJsonAsync<ItemImageDto>())!;
        var privateItem = await CreateItemAsync();
        var privateImage = (await (await _client.PostAsync($"/items/{privateItem.Id}/images", PngUpload()))
            .Content.ReadFromJsonAsync<ItemImageDto>())!;
        var share = await CreateShowcaseShareAsync();

        using var anonymous = _factory.CreateClient();
        var sharedResponse = await anonymous.GetAsync($"/public/{share.Slug}/media/{sharedImage.CardPath}");
        var privateResponse = await anonymous.GetAsync($"/public/{share.Slug}/media/{privateImage.CardPath}");

        sharedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        sharedResponse.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
        privateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Public_media_endpoint_rejects_an_expired_share()
    {
        var item = await CreateItemAsync(isShowcased: true);
        var image = (await (await _client.PostAsync($"/items/{item.Id}/images", PngUpload()))
            .Content.ReadFromJsonAsync<ItemImageDto>())!;
        var share = await CreateShowcaseShareAsync(DateTime.UtcNow.AddDays(-1));

        using var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync($"/public/{share.Slug}/media/{image.CardPath}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
