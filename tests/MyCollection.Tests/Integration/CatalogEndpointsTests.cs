using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MyCollection.Application.Categories;
using MyCollection.Application.Items;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class CatalogEndpointsTests(MongoFixture mongo) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await mongo.ResetAsync();
        _factory = new ApiFactory(mongo);
        _client = await AuthenticatedClient.CreateAsync(_factory, "owner@example.com");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<CategoryDto> CreateFigureCategoryAsync()
    {
        var response = await _client.PostAsJsonAsync("/categories", new
        {
            name = "公仔",
            icon = "figure",
            kind = "Physical",
            defaultDisplayMode = "List",
            fields = new[]
            {
                new { key = "brand", label = "廠商", type = "Select", options = (string[]?)["GSC", "ALTER"], required = true, searchable = true, showOnCard = true },
                new { key = "scale", label = "比例", type = "Text", options = (string[]?)null, required = false, searchable = false, showOnCard = false }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CategoryDto>())!;
    }

    private async Task<HttpResponseMessage> CreateItemAsync(string categoryId, string name, object attributes, string[]? tags = null) =>
        await _client.PostAsJsonAsync("/items", new
        {
            categoryId,
            name,
            description = (string?)null,
            tags = tags ?? [],
            isShowcased = false,
            attributes,
            acquisition = (object?)null
        });

    [Fact]
    public async Task Full_catalog_round_trip()
    {
        var category = await CreateFigureCategoryAsync();

        var created = await CreateItemAsync(category.Id, "初音ミク 1/8", new { brand = "GSC", scale = "1/8" }, ["VOCALOID"]);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var item = (await created.Content.ReadFromJsonAsync<ItemDto>())!;

        var fetched = await _client.GetFromJsonAsync<ItemDto>($"/items/{item.Id}");
        fetched!.Name.Should().Be("初音ミク 1/8");
        ((JsonElement)fetched.Attributes["brand"]!).GetString().Should().Be("GSC");

        var updated = await _client.PutAsJsonAsync($"/items/{item.Id}", new
        {
            categoryId = category.Id,
            name = "初音ミク 1/8（改）",
            description = "已開封",
            tags = new[] { "VOCALOID", "GSC" },
            isShowcased = true,
            attributes = new { brand = "ALTER" },
            acquisition = new { acquiredAt = "2026-01-01T00:00:00Z", amount = 12800, currency = "TWD", vendor = "GSC 官網" }
        });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);

        var deleted = await _client.DeleteAsync($"/items/{item.Id}");
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _client.GetAsync($"/items/{item.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Item_round_trips_display_mode_rating_and_storage_location()
    {
        var category = await CreateFigureCategoryAsync();

        var created = await _client.PostAsJsonAsync("/items", new
        {
            categoryId = category.Id,
            name = "初音ミク 1/8",
            description = (string?)null,
            tags = Array.Empty<string>(),
            isShowcased = true,
            attributes = new { brand = "GSC" },
            acquisition = (object?)null,
            displayMode = "Hero",
            rating = 9,
            storageLocation = "A櫃-第2層"
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var item = (await created.Content.ReadFromJsonAsync<ItemDto>())!;

        item.DisplayMode.Should().Be("Hero");
        item.EffectiveDisplayMode.Should().Be("Hero");
        item.Rating.Should().Be(9);
        item.StorageLocation.Should().Be("A櫃-第2層");

        var fetchedById = await _client.GetFromJsonAsync<ItemDto>($"/items/{item.Id}");
        fetchedById!.EffectiveDisplayMode.Should().Be("Hero");
        fetchedById.Rating.Should().Be(9);
        fetchedById.StorageLocation.Should().Be("A櫃-第2層");

        var searched = await _client.GetFromJsonAsync<PagedItemsResponse>("/items?pageSize=200");
        searched!.Items.Should().Contain(i => i.Id == item.Id).Which.EffectiveDisplayMode.Should().Be("Hero");
    }

    [Fact]
    public async Task Item_without_display_mode_override_inherits_the_category_default()
    {
        var category = await CreateFigureCategoryAsync();
        var created = await CreateItemAsync(category.Id, "初音ミク 1/7", new { brand = "GSC", scale = "1/7" });
        var item = (await created.Content.ReadFromJsonAsync<ItemDto>())!;

        item.DisplayMode.Should().BeNull();
        item.EffectiveDisplayMode.Should().Be("List", "CreateFigureCategoryAsync 的 DefaultDisplayMode 是 List");
    }

    [Fact]
    public async Task Item_violating_schema_returns_400_with_field_errors()
    {
        var category = await CreateFigureCategoryAsync();

        var response = await CreateItemAsync(category.Id, "無廠商", new { scale = "1/8" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").TryGetProperty("attributes.brand", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Item_with_value_outside_select_options_returns_400()
    {
        var category = await CreateFigureCategoryAsync();

        var response = await CreateItemAsync(category.Id, "未知廠商", new { brand = "MegaHouse" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Search_filters_by_tag_and_returns_paged_shape()
    {
        var category = await CreateFigureCategoryAsync();
        await CreateItemAsync(category.Id, "A", new { brand = "GSC" }, ["紅"]);
        await CreateItemAsync(category.Id, "B", new { brand = "GSC" }, ["藍"]);

        var result = await _client.GetFromJsonAsync<PagedItemsResponse>("/items?tags=紅&page=1&pageSize=10");

        result!.Total.Should().Be(1);
        result.Items.Should().ContainSingle().Which.Name.Should().Be("A");
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task Tags_endpoint_returns_distinct_tags()
    {
        var category = await CreateFigureCategoryAsync();
        await CreateItemAsync(category.Id, "A", new { brand = "GSC" }, ["紅", "限定"]);
        await CreateItemAsync(category.Id, "B", new { brand = "GSC" }, ["紅"]);

        var tags = await _client.GetFromJsonAsync<string[]>("/items/tags");

        tags.Should().BeEquivalentTo("紅", "限定");
    }

    [Fact]
    public async Task Another_user_cannot_see_or_modify_items()
    {
        var category = await CreateFigureCategoryAsync();
        var item = (await (await CreateItemAsync(category.Id, "我的公仔", new { brand = "GSC" }))
            .Content.ReadFromJsonAsync<ItemDto>())!;

        using var intruder = await AuthenticatedClient.CreateAsync(_factory, "intruder@example.com");

        (await intruder.GetAsync($"/items/{item.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.DeleteAsync($"/items/{item.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var list = await intruder.GetFromJsonAsync<PagedItemsResponse>("/items");
        list!.Total.Should().Be(0);
    }

    [Fact]
    public async Task Categories_endpoint_requires_authentication()
    {
        using var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync("/categories")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Api_startup_exposes_the_six_system_categories()
    {
        var categories = await _client.GetFromJsonAsync<CategoryDto[]>("/categories");

        categories!
            .Where(x => x.IsSystem)
            .Select(x => x.Name)
            .Should()
            .BeEquivalentTo("實體遊戲", "數位遊戲", "音樂專輯", "電影光碟", "公仔模型", "珍藏卡");
    }

    [Theory]
    [InlineData("/items/not-an-objectid")]
    [InlineData("/items/123")]
    public async Task Malformed_item_id_returns_404_not_500(string url)
    {
        (await _client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Malformed_category_id_delete_returns_404_not_500()
    {
        (await _client.DeleteAsync("/categories/not-an-objectid")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    private sealed record PagedItemsResponse(ItemDto[] Items, long Total, int Page, int PageSize);
}
