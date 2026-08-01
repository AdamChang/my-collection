using FluentAssertions;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoItemRepositoryEnrichmentTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly ObjectId Other = ObjectId.GenerateNewId();
    private static readonly ObjectId Category = ObjectId.GenerateNewId();

    private MongoItemRepository _sut = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        var userContext = new Mock<IUserContext>();
        userContext.SetupGet(c => c.UserId).Returns(Owner);

        _sut = new MongoItemRepository(fixture.Context, userContext.Object);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<ObjectId> InsertAsync(
        ObjectId ownerId, string name, string? steamAppId, long? igdbId)
    {
        var attributes = new BsonDocument();
        if (igdbId is not null)
        {
            attributes["igdbId"] = igdbId.Value;
        }

        var item = new Item
        {
            Id = ObjectId.GenerateNewId(),
            OwnerId = ownerId,
            CategoryId = Category,
            Name = name,
            Source = steamAppId is null ? ItemSource.Manual : ItemSource.Steam,
            ExternalRef = steamAppId is null
                ? null
                : new ExternalRef
                {
                    Provider = ProviderKeys.Steam,
                    ExternalId = steamAppId,
                    LastSyncedAt = DateTime.UtcNow
                },
            Attributes = attributes,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await fixture.Context.Items.InsertOneAsync(item);

        return item.Id;
    }

    [Fact]
    public async Task Lists_synced_items_that_lack_the_marker()
    {
        await InsertAsync(Owner, "TF2", "440", igdbId: null);
        await InsertAsync(Owner, "Portal 2", "620", igdbId: 1234);
        await InsertAsync(Owner, "手辦", null, igdbId: null);

        var candidates = await _sut.ListEnrichmentCandidatesAsync("igdbId", 50, CancellationToken.None);

        candidates.Select(i => i.Name).Should().BeEquivalentTo("TF2");
    }

    [Fact]
    public async Task Never_lists_another_owners_items()
    {
        await InsertAsync(Other, "別人的 TF2", "440", igdbId: null);

        var candidates = await _sut.ListEnrichmentCandidatesAsync("igdbId", 50, CancellationToken.None);

        candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task Honours_the_limit()
    {
        await InsertAsync(Owner, "A", "1", igdbId: null);
        await InsertAsync(Owner, "B", "2", igdbId: null);
        await InsertAsync(Owner, "C", "3", igdbId: null);

        var candidates = await _sut.ListEnrichmentCandidatesAsync("igdbId", 2, CancellationToken.None);

        candidates.Should().HaveCount(2);
    }

    [Fact]
    public async Task Loads_items_by_id()
    {
        var first = await InsertAsync(Owner, "A", "1", igdbId: 11);
        await InsertAsync(Owner, "B", "2", igdbId: 22);

        var items = await _sut.ListByIdsAsync([first], CancellationToken.None);

        items.Select(i => i.Name).Should().BeEquivalentTo("A");
    }

    [Fact]
    public async Task Loading_by_id_never_crosses_owners()
    {
        var otherItem = await InsertAsync(Other, "別人的", "1", igdbId: null);

        var items = await _sut.ListByIdsAsync([otherItem], CancellationToken.None);

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task Loading_an_empty_id_list_returns_nothing()
    {
        await InsertAsync(Owner, "A", "1", igdbId: null);

        (await _sut.ListByIdsAsync([], CancellationToken.None)).Should().BeEmpty();
    }
}
