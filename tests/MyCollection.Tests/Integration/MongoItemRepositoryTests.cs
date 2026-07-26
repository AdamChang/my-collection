using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoItemRepositoryTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly ObjectId OtherOwner = ObjectId.GenerateNewId();
    private static readonly ObjectId FigureCategory = ObjectId.GenerateNewId();
    private static readonly ObjectId GameCategory = ObjectId.GenerateNewId();

    private MongoItemRepository _sut = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        var userContext = new Mock<IUserContext>();
        userContext.SetupGet(c => c.UserId).Returns(Owner);
        _sut = new MongoItemRepository(fixture.Context, userContext.Object);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Item NewItem(
        ObjectId ownerId,
        string name,
        ObjectId categoryId,
        bool showcased = false,
        string[]? tags = null,
        string? description = null) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = ownerId,
        CategoryId = categoryId,
        Name = name,
        Description = description,
        Tags = (tags ?? []).ToList(),
        IsShowcased = showcased,
        Source = ItemSource.Manual,
        Attributes = new BsonDocument("brand", "GSC"),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private async Task SeedAsync()
    {
        await fixture.Context.Items.InsertManyAsync(
        [
            NewItem(Owner, "初音ミク Figure", FigureCategory, showcased: true, tags: ["GSC", "VOCALOID"]),
            NewItem(Owner, "Team Fortress 2", GameCategory, tags: ["FPS"], description: "Valve shooter"),
            NewItem(Owner, "Portal 2", GameCategory, showcased: true, tags: ["Puzzle"]),
            NewItem(OtherOwner, "別人的公仔", FigureCategory, showcased: true, tags: ["GSC"])
        ]);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_other_owners_item()
    {
        await SeedAsync();
        var foreign = await fixture.Context.Items
            .Find(MongoDB.Driver.Builders<Item>.Filter.Eq(x => x.OwnerId, OtherOwner)).FirstAsync();

        (await _sut.GetAsync(foreign.Id, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_never_returns_other_owners_items()
    {
        await SeedAsync();

        var result = await _sut.SearchAsync(new ItemQuerySpec(), CancellationToken.None);

        result.Total.Should().Be(3);
        result.Items.Should().NotContain(i => i.Name == "別人的公仔");
    }

    [Fact]
    public async Task SearchAsync_filters_by_category()
    {
        await SeedAsync();

        var result = await _sut.SearchAsync(new ItemQuerySpec { CategoryId = GameCategory }, CancellationToken.None);

        result.Total.Should().Be(2);
        result.Items.Select(i => i.Name).Should().BeEquivalentTo("Team Fortress 2", "Portal 2");
    }

    [Fact]
    public async Task SearchAsync_filters_by_showcased()
    {
        await SeedAsync();

        var result = await _sut.SearchAsync(new ItemQuerySpec { IsShowcased = true }, CancellationToken.None);

        result.Total.Should().Be(2);
    }

    [Fact]
    public async Task SearchAsync_filters_by_all_supplied_tags()
    {
        await SeedAsync();

        var both = await _sut.SearchAsync(new ItemQuerySpec { Tags = ["GSC", "VOCALOID"] }, CancellationToken.None);
        var missing = await _sut.SearchAsync(new ItemQuerySpec { Tags = ["GSC", "FPS"] }, CancellationToken.None);

        both.Total.Should().Be(1);
        missing.Total.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_full_text_matches_name_and_description()
    {
        await SeedAsync();

        var byName = await _sut.SearchAsync(new ItemQuerySpec { Search = "Portal" }, CancellationToken.None);
        var byDescription = await _sut.SearchAsync(new ItemQuerySpec { Search = "Valve" }, CancellationToken.None);

        byName.Items.Should().ContainSingle().Which.Name.Should().Be("Portal 2");
        byDescription.Items.Should().ContainSingle().Which.Name.Should().Be("Team Fortress 2");
    }

    [Fact]
    public async Task SearchAsync_pages_results()
    {
        await SeedAsync();

        var page1 = await _sut.SearchAsync(new ItemQuerySpec { Page = 1, PageSize = 2 }, CancellationToken.None);
        var page2 = await _sut.SearchAsync(new ItemQuerySpec { Page = 2, PageSize = 2 }, CancellationToken.None);

        page1.Total.Should().Be(3);
        page1.Items.Should().HaveCount(2);
        page2.Items.Should().HaveCount(1);
        page1.Items.Select(i => i.Id).Should().NotIntersectWith(page2.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task UpdateAsync_throws_NotFound_for_other_owners_item()
    {
        await SeedAsync();
        var foreign = await fixture.Context.Items
            .Find(MongoDB.Driver.Builders<Item>.Filter.Eq(x => x.OwnerId, OtherOwner)).FirstAsync();
        foreign.Name = "hijacked";

        var act = () => _sut.UpdateAsync(foreign, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_throws_NotFound_for_other_owners_item()
    {
        await SeedAsync();
        var foreign = await fixture.Context.Items
            .Find(MongoDB.Driver.Builders<Item>.Filter.Eq(x => x.OwnerId, OtherOwner)).FirstAsync();

        var act = () => _sut.DeleteAsync(foreign.Id, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ListTagsAsync_returns_distinct_owner_tags()
    {
        await SeedAsync();

        var tags = await _sut.ListTagsAsync(CancellationToken.None);

        tags.Should().BeEquivalentTo("FPS", "GSC", "Puzzle", "VOCALOID");
    }

    [Fact]
    public async Task SearchAsync_filters_by_attribute_values()
    {
        await fixture.Context.Items.InsertManyAsync(
        [
            NewItem(Owner, "GSC 公仔", FigureCategory),
            NewItem(Owner, "ALTER 公仔", FigureCategory)
        ]);
        await fixture.Context.Items.UpdateOneAsync(
            MongoDB.Driver.Builders<Item>.Filter.Eq(x => x.Name, "ALTER 公仔"),
            MongoDB.Driver.Builders<Item>.Update.Set("attributes.brand", "ALTER"));

        var result = await _sut.SearchAsync(
            new ItemQuerySpec { Attributes = new Dictionary<string, string> { ["brand"] = "ALTER" } },
            CancellationToken.None);

        result.Items.Should().ContainSingle().Which.Name.Should().Be("ALTER 公仔");
    }

    [Fact]
    public async Task SearchAsync_combines_attribute_filters_with_and()
    {
        await SeedAsync();

        var result = await _sut.SearchAsync(
            new ItemQuerySpec
            {
                Attributes = new Dictionary<string, string> { ["brand"] = "GSC", ["scale"] = "1/8" }
            },
            CancellationToken.None);

        result.Total.Should().Be(0, "沒有品項同時符合兩個屬性");
    }

    [Fact]
    public async Task SearchAsync_ignores_attribute_filters_with_blank_values()
    {
        await SeedAsync();

        var result = await _sut.SearchAsync(
            new ItemQuerySpec { Attributes = new Dictionary<string, string> { ["brand"] = "" } },
            CancellationToken.None);

        result.Total.Should().Be(3, "空值代表「不篩選」");
    }
}
