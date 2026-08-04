using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoItemEnrichWriterTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly ObjectId Category = ObjectId.GenerateNewId();
    private static readonly DateTime CreatedAt = new(2026, 7, 1, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EnrichedAt = new(2026, 8, 1, 3, 0, 0, DateTimeKind.Utc);

    private MongoItemEnrichWriter _sut = null!;
    private ObjectId _itemId;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        _sut = new MongoItemEnrichWriter(fixture.Context);
        _itemId = await InsertSteamItemAsync(Owner);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<ObjectId> InsertSteamItemAsync(ObjectId ownerId)
    {
        var item = new Item
        {
            Id = ObjectId.GenerateNewId(),
            OwnerId = ownerId,
            CategoryId = Category,
            Name = "Team Fortress 2",
            Description = null,
            Source = ItemSource.Steam,
            IsShowcased = true,
            Tags = ["最愛", "FPS"],
            Acquisition = new Acquisition { Vendor = "Steam 特賣" },
            Images = [new ItemImage { Id = "img1", Path = "a", CardPath = "b", ThumbPath = "c" }],
            ExternalRef = new ExternalRef
            {
                Provider = ProviderKeys.Steam, ExternalId = "440", LastSyncedAt = CreatedAt
            },
            Attributes = new BsonDocument { { "playtimeForever", 1234 } },
            CreatedAt = CreatedAt,
            UpdatedAt = CreatedAt
        };

        await fixture.Context.Items.InsertOneAsync(item);

        return item.Id;
    }

    private Task<Item> LoadAsync(ObjectId id) =>
        fixture.Context.Items.Find(Builders<Item>.Filter.Eq(x => x.Id, id)).FirstAsync();

    private static ItemEnrichment Enrichment(
        ObjectId itemId, string? description = null, string? name = null) =>
        new(itemId, name, description, new Dictionary<string, object?>
        {
            ["igdbId"] = 1942L,
            ["developer"] = "Valve",
            ["igdbRating"] = 93.5d
        });

    [Fact]
    public async Task Writes_the_provider_attributes()
    {
        var matched = await _sut.ApplyAsync(
            Owner, [Enrichment(_itemId)], EnrichedAt, ProviderKeys.Igdb, CancellationToken.None);

        matched.Should().Be(1);

        var item = await LoadAsync(_itemId);
        item.Attributes["igdbId"].AsInt64.Should().Be(1942);
        item.Attributes["developer"].AsString.Should().Be("Valve");
        item.Attributes["igdbRating"].AsDouble.Should().Be(93.5);
        item.UpdatedAt.Should().Be(EnrichedAt);
    }

    [Fact]
    public async Task Preserves_attributes_written_by_other_providers()
    {
        await _sut.ApplyAsync(Owner, [Enrichment(_itemId)], EnrichedAt, ProviderKeys.Igdb, CancellationToken.None);

        (await LoadAsync(_itemId)).Attributes["playtimeForever"].AsInt32.Should().Be(1234);
    }

    [Fact]
    public async Task Never_touches_fields_the_user_owns()
    {
        await _sut.ApplyAsync(Owner, [Enrichment(_itemId)], EnrichedAt, ProviderKeys.Igdb, CancellationToken.None);

        var item = await LoadAsync(_itemId);
        item.Name.Should().Be("Team Fortress 2", "Steam 的名稱是使用者在庫裡認得的那個");
        item.IsShowcased.Should().BeTrue();
        item.Tags.Should().BeEquivalentTo("最愛", "FPS");
        item.Acquisition!.Vendor.Should().Be("Steam 特賣");
        item.Images.Should().ContainSingle();
        item.CreatedAt.Should().Be(CreatedAt);
        item.Source.Should().Be(ItemSource.Steam);
    }

    [Fact]
    public async Task Writes_the_description_when_one_is_supplied()
    {
        await _sut.ApplyAsync(
            Owner, [Enrichment(_itemId, "A team-based shooter.")], EnrichedAt, ProviderKeys.Igdb, CancellationToken.None);

        (await LoadAsync(_itemId)).Description.Should().Be("A team-based shooter.");
    }

    [Fact]
    public async Task Leaves_the_description_alone_when_none_is_supplied()
    {
        await fixture.Context.Items.UpdateOneAsync(
            Builders<Item>.Filter.Eq(x => x.Id, _itemId),
            Builders<Item>.Update.Set(x => x.Description, "我自己寫的心得"));

        await _sut.ApplyAsync(Owner, [Enrichment(_itemId)], EnrichedAt, ProviderKeys.Igdb, CancellationToken.None);

        (await LoadAsync(_itemId)).Description.Should().Be("我自己寫的心得");
    }

    [Fact]
    public async Task Never_touches_another_owners_item()
    {
        var otherOwner = ObjectId.GenerateNewId();
        var otherItemId = await InsertSteamItemAsync(otherOwner);

        var matched = await _sut.ApplyAsync(
            Owner, [Enrichment(otherItemId)], EnrichedAt, ProviderKeys.Igdb, CancellationToken.None);

        matched.Should().Be(0);
        (await LoadAsync(otherItemId)).Attributes.Should().NotContain(e => e.Name == "igdbId");
    }

    [Fact]
    public async Task Never_creates_an_item_for_an_id_that_does_not_exist()
    {
        var matched = await _sut.ApplyAsync(
            Owner, [Enrichment(ObjectId.GenerateNewId())], EnrichedAt, ProviderKeys.Igdb, CancellationToken.None);

        matched.Should().Be(0);
        (await fixture.Context.Items.CountDocumentsAsync(FilterDefinition<Item>.Empty)).Should().Be(1);
    }

    [Fact]
    public async Task Empty_input_is_a_no_op()
    {
        (await _sut.ApplyAsync(Owner, [], EnrichedAt, ProviderKeys.Igdb, CancellationToken.None))
            .Should().Be(0);
    }

    [Fact]
    public async Task An_enrichment_with_nothing_to_write_is_skipped_entirely()
    {
        var empty = new ItemEnrichment(_itemId, null, null, new Dictionary<string, object?>());

        var matched = await _sut.ApplyAsync(Owner, [empty], EnrichedAt, ProviderKeys.Igdb, CancellationToken.None);

        matched.Should().Be(0);
        (await LoadAsync(_itemId)).UpdatedAt.Should().Be(CreatedAt, "沒有東西要寫就不該動 updatedAt");
    }
}
