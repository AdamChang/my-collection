using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MyCollection.Application.Categories;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class CategoryEndpointsTests(MongoFixture mongo) : IAsyncLifetime
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

    private async Task<CategoryDto> CreateCategoryAsync(params string[] keys)
    {
        var response = await _client.PostAsJsonAsync("/categories", new
        {
            name = "公仔",
            icon = "figure",
            kind = "Physical",
            defaultDisplayMode = "List",
            fields = keys.Select(k => new { key = k, label = k, type = "Text", options = (string[]?)null, required = false, searchable = false, showOnCard = false })
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CategoryDto>())!;
    }

    private async Task<string> CreateItemAsync(string categoryId, object attributes)
    {
        var response = await _client.PostAsJsonAsync("/items", new
        {
            categoryId, name = "x", description = (string?)null, tags = Array.Empty<string>(),
            isShowcased = false, attributes, acquisition = (object?)null
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Rename_moves_item_attributes_and_reports_count()
    {
        var category = await CreateCategoryAsync("price", "brand");
        var itemId = await CreateItemAsync(category.Id, new { price = "100", brand = "GSC" });

        var response = await _client.PostAsJsonAsync($"/categories/{category.Id}/fields/price/rename", new { newKey = "purchasePrice" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<RenameFieldResultDto>())!;
        result.MovedItemCount.Should().Be(1);
        result.Category.Fields.Select(f => f.Key).Should().Equal("purchasePrice", "brand");

        var item = await _client.GetFromJsonAsync<JsonElement>($"/items/{itemId}");
        var attributes = item.GetProperty("attributes");
        attributes.GetProperty("purchasePrice").GetString().Should().Be("100");
        attributes.TryGetProperty("price", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Rename_to_declared_key_is_400()
    {
        var category = await CreateCategoryAsync("price", "purchasePrice");

        var response = await _client.PostAsJsonAsync($"/categories/{category.Id}/fields/price/rename", new { newKey = "purchasePrice" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("NewKey");
    }

    [Fact]
    public async Task Rename_of_undeclared_key_is_404()
    {
        var category = await CreateCategoryAsync("brand");

        var response = await _client.PostAsJsonAsync($"/categories/{category.Id}/fields/price/rename", new { newKey = "purchasePrice" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_category_with_items_is_409()
    {
        var category = await CreateCategoryAsync("brand");
        await CreateItemAsync(category.Id, new { brand = "GSC" });

        var response = await _client.DeleteAsync($"/categories/{category.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("1 item");
    }

    [Fact]
    public async Task Put_that_withdraws_provider_field_is_400()
    {
        var category = await CreateCategoryAsync("steamAppId", "brand");

        var response = await _client.PutAsJsonAsync($"/categories/{category.Id}", new
        {
            name = "公仔", icon = "figure", kind = "Physical", defaultDisplayMode = "List",
            fields = new[] { new { key = "brand", label = "brand", type = "Text", options = (string[]?)null, required = false, searchable = false, showOnCard = false } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("steamAppId");
    }
}
