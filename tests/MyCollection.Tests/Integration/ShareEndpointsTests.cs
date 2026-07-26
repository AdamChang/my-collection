using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MyCollection.Application.Categories;
using MyCollection.Application.Sharing;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class ShareEndpointsTests(MongoFixture mongo) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await mongo.ResetAsync();
        _factory = new ApiFactory(mongo);
        _client = await AuthenticatedClient.CreateAsync(_factory, "sharer@example.com");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<string> SeedShowcasedItemAsync()
    {
        var category = (await (await _client.PostAsJsonAsync("/categories", new
        {
            name = "公仔", icon = "figure", kind = "Physical", fields = Array.Empty<object>()
        })).Content.ReadFromJsonAsync<CategoryDto>())!;

        await _client.PostAsJsonAsync("/items", new
        {
            categoryId = category.Id, name = "精選公仔", description = "描述",
            tags = new[] { "GSC" }, isShowcased = true, attributes = new { },
            acquisition = new { acquiredAt = "2026-01-01T00:00:00Z", amount = 12800, currency = "TWD", vendor = "GSC 官網" }
        });

        await _client.PostAsJsonAsync("/items", new
        {
            categoryId = category.Id, name = "非精選公仔", description = (string?)null,
            tags = Array.Empty<string>(), isShowcased = false, attributes = new { }, acquisition = (object?)null
        });

        return category.Id;
    }

    private async Task<ShareLinkDto> CreateShareAsync(bool includePrice = false)
    {
        var response = await _client.PostAsJsonAsync("/shares", new
        {
            scope = "Showcase", includeCategoryIds = Array.Empty<string>(), includePrice, expiresAt = (DateTime?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ShareLinkDto>())!;
    }

    [Fact]
    public async Task Public_page_is_anonymous_and_shows_only_showcased_items()
    {
        await SeedShowcasedItemAsync();
        var share = await CreateShareAsync();

        using var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync($"/public/{share.Slug}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = (await response.Content.ReadFromJsonAsync<PublicShareDto>())!;
        payload.Items.Should().ContainSingle().Which.Name.Should().Be("精選公仔");
        payload.OwnerDisplayName.Should().Be("Tester");
    }

    [Fact]
    public async Task Public_payload_never_contains_acquisition_by_default()
    {
        await SeedShowcasedItemAsync();
        var share = await CreateShareAsync(includePrice: false);

        using var anonymous = _factory.CreateClient();
        var raw = await anonymous.GetStringAsync($"/public/{share.Slug}");

        raw.Should().NotContain("acquisition");
        raw.Should().NotContain("12800");
        raw.Should().NotContain("GSC 官網");
        raw.Should().NotContain("acquiredAt");
    }

    [Fact]
    public async Task Public_payload_contains_price_only_when_share_opts_in()
    {
        await SeedShowcasedItemAsync();
        var share = await CreateShareAsync(includePrice: true);

        using var anonymous = _factory.CreateClient();
        var raw = await anonymous.GetStringAsync($"/public/{share.Slug}");

        raw.Should().Contain("12800");
        raw.Should().NotContain("GSC 官網", "vendor 永遠不外流");
        raw.Should().NotContain("acquiredAt", "購入日期永遠不外流");
    }

    [Fact]
    public async Task Unknown_slug_returns_404()
    {
        using var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync("/public/doesnotexist")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Expired_share_returns_404()
    {
        await SeedShowcasedItemAsync();
        var response = await _client.PostAsJsonAsync("/shares", new
        {
            scope = "Showcase",
            includeCategoryIds = Array.Empty<string>(),
            includePrice = false,
            expiresAt = DateTime.UtcNow.AddDays(-1)
        });
        var share = (await response.Content.ReadFromJsonAsync<ShareLinkDto>())!;

        using var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync($"/public/{share.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleted_share_stops_resolving()
    {
        await SeedShowcasedItemAsync();
        var share = await CreateShareAsync();

        (await _client.DeleteAsync($"/shares/{share.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync($"/public/{share.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Showcase_endpoint_returns_only_showcased_items()
    {
        await SeedShowcasedItemAsync();

        var result = await _client.GetFromJsonAsync<JsonElement>("/showcase");

        result.GetProperty("total").GetInt64().Should().Be(1);
        result.GetProperty("items")[0].GetProperty("name").GetString().Should().Be("精選公仔");
    }
}
