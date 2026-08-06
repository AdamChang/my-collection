using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Application.Sharing;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoPublicCatalogReaderTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly ObjectId OtherOwner = ObjectId.GenerateNewId();
    private static readonly ObjectId FigureCategory = ObjectId.GenerateNewId();
    private static readonly ObjectId GameCategory = ObjectId.GenerateNewId();

    private MongoPublicCatalogReader _sut = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        _sut = new MongoPublicCatalogReader(fixture.Context);
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Item NewItem(ObjectId ownerId, string name, ObjectId categoryId, bool showcased) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = ownerId,
        CategoryId = categoryId,
        Name = name,
        Description = "描述",
        Tags = ["tag"],
        IsShowcased = showcased,
        Attributes = new BsonDocument("brand", "GSC"),
        Acquisition = new Acquisition
        {
            AcquiredAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Price = new Money(12800m, "TWD"),
            Vendor = "GSC 官網"
        },
        Rating = 9,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private async Task SeedAsync()
    {
        await fixture.Context.Items.InsertManyAsync(
        [
            NewItem(Owner, "精選公仔", FigureCategory, showcased: true),
            NewItem(Owner, "非精選公仔", FigureCategory, showcased: false),
            NewItem(Owner, "精選遊戲", GameCategory, showcased: true),
            NewItem(OtherOwner, "別人的精選", FigureCategory, showcased: true)
        ]);

        // CreatedAt / UpdatedAt 必須明確賦值：UtcOnlyDateTimeSerializer 會拒絕
        // Kind = Unspecified 的 DateTime.MinValue。
        await fixture.Context.Categories.InsertManyAsync(
        [
            new Category
            {
                Id = FigureCategory, OwnerId = Owner, Name = "公仔", Icon = "figure",
                DefaultDisplayMode = DisplayMode.Hero,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            },
            new Category
            {
                Id = GameCategory, OwnerId = Owner, Name = "數位遊戲", Icon = "game",
                DefaultDisplayMode = DisplayMode.Stats,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            }
        ]);
    }

    [Fact]
    public async Task Showcase_scope_returns_only_showcased_items_of_that_owner()
    {
        var items = await _sut.ListItemsAsync(Owner, ShareScope.Showcase, [], includePrice: false, includeRating: false, CancellationToken.None);

        items.Select(i => i.Name).Should().BeEquivalentTo("精選公仔", "精選遊戲");
    }

    [Fact]
    public async Task Category_scope_returns_all_items_of_the_listed_categories()
    {
        var items = await _sut.ListItemsAsync(Owner, ShareScope.Category, [FigureCategory], includePrice: false, includeRating: false, CancellationToken.None);

        items.Select(i => i.Name).Should().BeEquivalentTo("精選公仔", "非精選公仔");
    }

    [Fact]
    public async Task Projection_excludes_acquisition_entirely_when_price_not_included()
    {
        var items = await _sut.ListItemsAsync(Owner, ShareScope.Showcase, [], includePrice: false, includeRating: false, CancellationToken.None);

        items.Should().OnlyContain(i => i.Price == null);
        items.Should().OnlyContain(i => i.AcquiredAt == null);
        items.Should().OnlyContain(i => i.Name.Length > 0);
    }

    [Fact]
    public async Task Projection_includes_price_and_acquired_at_only_when_explicitly_enabled()
    {
        var items = await _sut.ListItemsAsync(Owner, ShareScope.Showcase, [], includePrice: true, includeRating: false, CancellationToken.None);

        items.Should().OnlyContain(i => i.Price != null);
        items[0].Price!.Amount.Should().Be(12800m);
        items[0].Price!.Currency.Should().Be("TWD");
        items.Should().OnlyContain(i => i.AcquiredAt == new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Projection_includes_rating_only_when_explicitly_enabled()
    {
        var withoutRating = await _sut.ListItemsAsync(Owner, ShareScope.Showcase, [], includePrice: false, includeRating: false, CancellationToken.None);
        var withRating = await _sut.ListItemsAsync(Owner, ShareScope.Showcase, [], includePrice: false, includeRating: true, CancellationToken.None);

        withoutRating.Should().OnlyContain(i => i.Rating == null);
        withRating.Should().OnlyContain(i => i.Rating == 9);
    }

    [Fact]
    public async Task ListCategoriesAsync_maps_ids_to_name_and_default_display_mode()
    {
        var categories = await _sut.ListCategoriesAsync(Owner, CancellationToken.None);

        categories[FigureCategory].Name.Should().Be("公仔");
        categories[FigureCategory].DefaultDisplayMode.Should().Be(DisplayMode.Hero);
        categories[GameCategory].Name.Should().Be("數位遊戲");
        categories[GameCategory].DefaultDisplayMode.Should().Be(DisplayMode.Stats);
    }
}
